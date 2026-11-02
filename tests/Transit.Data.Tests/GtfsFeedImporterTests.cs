using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Transit.Gtfs.Static;

namespace Transit.Data.Tests;

public sealed class GtfsFeedImporterTests(PostgresFixture database) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly Uri SourceUrl = new("https://feeds.example.test/gtfs.zip");
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly (string Name, string Content)[] SampleFiles =
    [
        ("feed_info.txt",
            "feed_publisher_name,feed_version,feed_start_date,feed_end_date\n" +
            "Example,\"Fall 2026, version D\",20261023,20261212\n"),
        ("agency.txt",
            "agency_id,agency_name,agency_url,agency_timezone\n" +
            "1,Example Transit,https://example.test,America/New_York\n"),
        ("routes.txt",
            "route_id,agency_id,route_short_name,route_long_name,route_type\n" +
            "42,1,42,Forest Hills - Dudley,3\n" +
            "Red,1,,Red Line,1\n"),
        ("stops.txt",
            "stop_id,stop_name,stop_lat,stop_lon,location_type,parent_station\n" +
            "place-sstat,South Station,42.352271,-71.055242,1,\n" +
            "70080,South Station - Red Line,42.352271,-71.055242,0,place-sstat\n" +
            "door-1,,,,2,place-sstat\n"),
        ("trips.txt",
            "route_id,service_id,trip_id,trip_headsign,direction_id,block_id,shape_id\n" +
            "Red,weekday,trip-1,Alewife,1,B1,shape-1\n" +
            "42,weekday,trip-2,,,,\n"),
        ("stop_times.txt",
            "trip_id,arrival_time,departure_time,stop_id,stop_sequence\n" +
            "trip-1,24:55:00,24:56:00,70080,1\n" +
            "trip-1,25:10:00,25:10:30,place-sstat,2\n" +
            "trip-2,,,70080,1\n"),
        ("calendar.txt",
            "service_id,monday,tuesday,wednesday,thursday,friday,saturday,sunday,start_date,end_date\n" +
            "weekday,1,1,1,1,1,0,0,20261023,20261211\n"),
        ("calendar_dates.txt",
            "service_id,date,exception_type\n" +
            "weekday,20261126,2\n"),
    ];

    public async Task InitializeAsync()
    {
        await using var context = database.CreateContext();
        await context.GtfsFeeds.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Imports_every_schedule_table_under_a_new_feed()
    {
        var result = await ImportAsync(SampleFiles, "hash-1");

        Assert.Equal(
            new GtfsImportResult(result.FeedId, "Fall 2026, version D", 1, 2, 3, 2, 3, 1, 1),
            result);

        await using var context = database.CreateContext();
        var feed = await context.GtfsFeeds.SingleAsync();
        Assert.Equal(result.FeedId, feed.Id);
        Assert.Equal("hash-1", feed.Sha256);
        Assert.Equal(SourceUrl.ToString(), feed.SourceUrl);
        Assert.Equal(new DateOnly(2026, 10, 23), feed.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 12), feed.EndDate);
        Assert.Equal(Now, feed.ImportedAt);
        Assert.Equal("America/New_York", (await context.Agencies.SingleAsync()).TimeZone);
    }

    [Fact]
    public async Task Stores_times_past_midnight_and_missing_values()
    {
        var result = await ImportAsync(SampleFiles, "hash-1");

        await using var context = database.CreateContext();
        var stopTimes = await context.StopTimes
            .Where(s => s.FeedId == result.FeedId)
            .OrderBy(s => s.TripId).ThenBy(s => s.StopSequence)
            .ToListAsync();

        Assert.Equal(new TimeSpan(25, 10, 0), stopTimes[1].ArrivalTime);
        Assert.Equal(new TimeSpan(25, 10, 30), stopTimes[1].DepartureTime);
        Assert.Null(stopTimes[2].ArrivalTime);
        Assert.Null(stopTimes[2].DepartureTime);

        var door = await context.Stops.SingleAsync(s => s.StopId == "door-1");
        Assert.Null(door.Name);
        Assert.Null(door.Latitude);
        Assert.Equal(2, door.LocationType);
        Assert.Equal("place-sstat", door.ParentStation);

        var trip = await context.Trips.SingleAsync(t => t.TripId == "trip-2");
        Assert.Null(trip.DirectionId);
        Assert.Null(trip.Headsign);

        var exception = await context.CalendarDates.SingleAsync();
        Assert.Equal(CalendarExceptionType.Removed, exception.ExceptionType);
    }

    [Fact]
    public async Task Recognises_a_feed_that_was_already_imported()
    {
        await using var context = database.CreateContext();
        var importer = new GtfsFeedImporter(context, new FixedTimeProvider(Now));

        Assert.False(await importer.IsImportedAsync("hash-1", CancellationToken.None));

        await ImportAsync(SampleFiles, "hash-1");

        Assert.True(await importer.IsImportedAsync("hash-1", CancellationToken.None));
    }

    [Fact]
    public async Task Keeps_earlier_feeds_when_a_new_version_is_imported()
    {
        var first = await ImportAsync(SampleFiles, "hash-1");
        var second = await ImportAsync(SampleFiles, "hash-2");

        await using var context = database.CreateContext();
        Assert.NotEqual(first.FeedId, second.FeedId);
        Assert.Equal(2, await context.GtfsFeeds.CountAsync());
        Assert.Equal(6, await context.StopTimes.CountAsync());
    }

    [Fact]
    public async Task Leaves_no_partial_feed_when_a_file_is_invalid()
    {
        var files = SampleFiles
            .Select(file => file.Name == "stop_times.txt"
                ? (file.Name, file.Content + "trip-2,8:61:00,8:61:00,70080,2\n")
                : file)
            .ToArray();

        await Assert.ThrowsAsync<FormatException>(() => ImportAsync(files, "hash-1"));

        await using var context = database.CreateContext();
        Assert.Equal(0, await context.GtfsFeeds.CountAsync());
        Assert.Equal(0, await context.Routes.CountAsync());
        Assert.Equal(0, await context.StopTimes.CountAsync());
    }

    private async Task<GtfsImportResult> ImportAsync((string Name, string Content)[] files, string sha256)
    {
        await using var context = database.CreateContext();
        using var feed = Feed(files);
        return await new GtfsFeedImporter(context, new FixedTimeProvider(Now))
            .ImportAsync(feed, sha256, SourceUrl, CancellationToken.None);
    }

    private static GtfsStaticFeed Feed((string Name, string Content)[] files)
    {
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        zip.Position = 0;
        return GtfsStaticFeed.Open(zip);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
