using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Transit.Gtfs;
using Transit.Gtfs.Arrivals;

namespace Transit.Data.Tests;

public sealed class StopArrivalRecorderTests(PostgresFixture database) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateOnly ServiceDate = new(2026, 6, 1);
    private static readonly DateTimeOffset EightAmBoston = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(3);
    private static readonly string[] StopIds = ["A", "B", "C"];

    public async Task InitializeAsync()
    {
        await using var context = database.CreateContext();
        await context.StopArrivalRuns.ExecuteDeleteAsync();
        await context.VehiclePositions.ExecuteDeleteAsync();
        await context.GtfsFeeds.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Records_arrivals_with_scheduled_times_resolved_in_the_agency_time_zone()
    {
        var feedId = await AddFeedAsync("hash-1", EightAmBoston.AddDays(-1), "08:00:00", "08:02:00", "08:04:00");
        await AddPositionsAsync(
            Position("trip-1", 0, 1, VehicleStopStatus.StoppedAt),
            Position("trip-1", 150, 2, VehicleStopStatus.StoppedAt),
            Position("trip-1", 170, 3, VehicleStopStatus.InTransitTo),
            Position("trip-1", 290, 3, VehicleStopStatus.StoppedAt));

        var result = await RecordAsync();

        Assert.Equal(new StopArrivalRunResult(ServiceDate, 1, 1, 2), result);

        var arrivals = await ReadArrivalsAsync();
        Assert.Collection(
            arrivals,
            b =>
            {
                Assert.Equal(("trip-1", 2, "B", "42", feedId), (b.TripId, b.StopSequence, b.StopId, b.RouteId, b.FeedId));
                Assert.Equal(EightAmBoston.AddMinutes(2), b.ScheduledAt);
                Assert.Equal(EightAmBoston.AddSeconds(150), b.ObservedAt);
                Assert.Equal(ArrivalEvidence.ReportedStopped, b.Evidence);
            },
            c =>
            {
                Assert.Equal(3, c.StopSequence);
                Assert.Equal(EightAmBoston.AddMinutes(4), c.ScheduledAt);
                Assert.Equal(EightAmBoston.AddSeconds(290), c.ObservedAt);
            });
    }

    [Fact]
    public async Task Counts_trips_missing_from_the_schedule_as_observed_but_unmatched()
    {
        await AddFeedAsync("hash-1", EightAmBoston.AddDays(-1), "08:00:00", "08:02:00", "08:04:00");
        await AddPositionsAsync(
            Position("added-trip", 0, 1, VehicleStopStatus.StoppedAt),
            Position("added-trip", 20, 2, VehicleStopStatus.StoppedAt));

        var result = await RecordAsync();

        Assert.Equal(new StopArrivalRunResult(ServiceDate, 1, 0, 0), result);
        Assert.Empty(await ReadArrivalsAsync());
    }

    [Fact]
    public async Task Uses_the_newest_schedule_imported_before_the_trip_was_first_seen()
    {
        await AddFeedAsync("hash-old", EightAmBoston.AddDays(-2), "08:00:00", "08:02:00", "08:04:00");
        await AddFeedAsync("hash-current", EightAmBoston.AddDays(-1), "08:00:00", "08:05:00", "08:10:00");
        await AddFeedAsync("hash-later", EightAmBoston.AddHours(1), "08:00:00", "08:30:00", "09:00:00");
        await AddPositionsAsync(
            Position("trip-1", 0, 1, VehicleStopStatus.StoppedAt),
            Position("trip-1", 300, 2, VehicleStopStatus.StoppedAt));

        await RecordAsync();

        var arrival = Assert.Single(await ReadArrivalsAsync());
        Assert.Equal(EightAmBoston.AddMinutes(5), arrival.ScheduledAt);
    }

    [Fact]
    public async Task Recording_a_service_date_again_replaces_its_arrivals_and_run()
    {
        await AddFeedAsync("hash-1", EightAmBoston.AddDays(-1), "08:00:00", "08:02:00", "08:04:00");
        await AddPositionsAsync(
            Position("trip-1", 0, 1, VehicleStopStatus.StoppedAt),
            Position("trip-1", 120, 2, VehicleStopStatus.StoppedAt));

        await RecordAsync();
        await AddPositionsAsync(Position("trip-1", 240, 3, VehicleStopStatus.StoppedAt));
        var second = await RecordAsync();

        Assert.Equal(2, second.ArrivalsRecorded);
        Assert.Equal([2, 3], (await ReadArrivalsAsync()).Select(a => a.StopSequence));

        await using var context = database.CreateContext();
        var run = await context.StopArrivalRuns.SingleAsync();
        Assert.Equal(2, run.ArrivalsRecorded);
    }

    [Fact]
    public async Task Reports_which_service_dates_have_been_recorded()
    {
        await RecordAsync();

        await using var context = database.CreateContext();
        var recorder = new StopArrivalRecorder(context, new FakeTimeProvider());
        var recorded = await recorder.GetRecordedServiceDatesAsync(
            ServiceDate.AddDays(-1),
            ServiceDate.AddDays(1),
            CancellationToken.None);

        Assert.Equal([ServiceDate], recorded);
    }

    private async Task<StopArrivalRunResult> RecordAsync()
    {
        await using var context = database.CreateContext();
        var recorder = new StopArrivalRecorder(context, new FakeTimeProvider(EightAmBoston.AddDays(1)));
        return await recorder.RecordAsync(ServiceDate, MaxGap, CancellationToken.None);
    }

    private async Task<List<ObservedStopArrival>> ReadArrivalsAsync()
    {
        await using var context = database.CreateContext();
        return await context.ObservedStopArrivals
            .AsNoTracking()
            .OrderBy(a => a.TripId)
            .ThenBy(a => a.StopSequence)
            .ToListAsync();
    }

    private async Task<int> AddFeedAsync(string sha256, DateTimeOffset importedAt, params string[] stopArrivals)
    {
        await using var context = database.CreateContext();

        var feed = new GtfsFeed { Sha256 = sha256, SourceUrl = "https://feeds.example.test/gtfs.zip", ImportedAt = importedAt };
        context.GtfsFeeds.Add(feed);
        await context.SaveChangesAsync();

        context.Agencies.Add(new Agency { FeedId = feed.Id, Name = "Example Transit", TimeZone = "America/New_York" });
        context.Trips.Add(new Trip { FeedId = feed.Id, TripId = "trip-1", RouteId = "42", ServiceId = "weekday" });
        context.StopTimes.AddRange(stopArrivals.Select((arrival, i) => new StopTime
        {
            FeedId = feed.Id,
            TripId = "trip-1",
            StopSequence = i + 1,
            StopId = StopIds[i],
            ArrivalTime = GtfsTime.Parse(arrival).SinceServiceDayStart,
        }));
        await context.SaveChangesAsync();

        return feed.Id;
    }

    private async Task AddPositionsAsync(params VehiclePosition[] positions)
    {
        await using var context = database.CreateContext();
        await new VehiclePositionWriter(context).InsertNewAsync(positions, CancellationToken.None);
    }

    private static VehiclePosition Position(string tripId, int secondsAfterEight, int stopSequence, VehicleStopStatus status) =>
        new()
        {
            VehicleId = "y1",
            TripId = tripId,
            RouteId = "42",
            StartDate = ServiceDate,
            Latitude = 42.35,
            Longitude = -71.05,
            ObservedAt = EightAmBoston.AddSeconds(secondsAfterEight),
            CurrentStopSequence = stopSequence,
            CurrentStatus = status,
        };
}
