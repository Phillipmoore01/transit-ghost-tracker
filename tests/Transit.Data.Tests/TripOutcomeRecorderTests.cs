using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Transit.Gtfs;
using Transit.Gtfs.Reliability;
using Transit.Gtfs.Static;

namespace Transit.Data.Tests;

public sealed class TripOutcomeRecorderTests(PostgresFixture database) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateOnly Monday = new(2026, 6, 1);
    private static readonly DateTimeOffset MidnightBoston = new(2026, 6, 1, 4, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxFeedGap = TimeSpan.FromMinutes(2);

    private static readonly ScheduledTripSpec[] Trips =
    [
        new("observed", "42", "weekday", "08:00:00", "08:10:00"),
        new("missing", "42", "weekday", "09:00:00", "09:10:00"),
        new("during-outage", "42", "weekday", "10:00:00", "10:10:00"),
        new("untracked-route", "99", "weekday", "09:00:00", "09:10:00"),
        new("weekend-only", "42", "weekend", "09:00:00", "09:10:00"),
        new("added-by-exception", "42", "special", "08:30:00", "08:40:00"),
        new("removed-by-exception", "42", "weekday-except-today", "08:30:00", "08:40:00"),
    ];

    public async Task InitializeAsync()
    {
        await using var context = database.CreateContext();
        await context.TripOutcomeRuns.ExecuteDeleteAsync();
        await context.FeedPolls.ExecuteDeleteAsync();
        await context.VehiclePositions.ExecuteDeleteAsync();
        await context.GtfsFeeds.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Classifies_every_trip_scheduled_on_the_service_date()
    {
        var feedId = await AddFeedAsync("hash-1", MidnightBoston.AddDays(-1));
        await AddPollsAsync(MidnightBoston.AddHours(7.5), MidnightBoston.AddHours(9.5));
        await AddPositionAsync("observed", MidnightBoston.AddHours(8).AddMinutes(1));

        var result = await RecordAsync();

        Assert.Equal(new TripOutcomeRunResult(Monday, feedId, 5, 1, 2, 1, 1), result);

        await using var context = database.CreateContext();
        var outcomes = await context.ScheduledTripOutcomes.AsNoTracking().ToDictionaryAsync(o => o.TripId);

        Assert.Equal(TripOutcome.Observed, outcomes["observed"].Outcome);
        Assert.Equal(MidnightBoston.AddHours(8).AddMinutes(1), outcomes["observed"].FirstObservedAt);
        Assert.Equal(TripOutcome.NotObserved, outcomes["missing"].Outcome);
        Assert.Null(outcomes["missing"].FirstObservedAt);
        Assert.Equal(MidnightBoston.AddHours(9), outcomes["missing"].ScheduledStart);
        Assert.Equal(MidnightBoston.AddHours(9).AddMinutes(10), outcomes["missing"].ScheduledEnd);
        Assert.Equal(TripOutcome.NotObserved, outcomes["added-by-exception"].Outcome);
        Assert.Equal(TripOutcome.FeedGap, outcomes["during-outage"].Outcome);
        Assert.Equal(TripOutcome.RouteNotTracked, outcomes["untracked-route"].Outcome);
        Assert.DoesNotContain("weekend-only", outcomes.Keys);
        Assert.DoesNotContain("removed-by-exception", outcomes.Keys);
    }

    [Fact]
    public async Task Uses_the_newest_schedule_imported_before_the_service_day_began()
    {
        await AddFeedAsync("hash-old", MidnightBoston.AddDays(-7));
        var current = await AddFeedAsync("hash-current", MidnightBoston.AddDays(-1));
        await AddFeedAsync("hash-later", MidnightBoston.AddHours(12));

        var result = await RecordAsync();

        Assert.Equal(current, result?.FeedId);
    }

    [Fact]
    public async Task Records_nothing_when_no_schedule_covers_the_service_date()
    {
        Assert.Null(await RecordAsync());

        await using var context = database.CreateContext();
        Assert.False(await context.TripOutcomeRuns.AnyAsync());
    }

    [Fact]
    public async Task Recording_a_service_date_again_replaces_its_outcomes()
    {
        await AddFeedAsync("hash-1", MidnightBoston.AddDays(-1));
        await AddPollsAsync(MidnightBoston.AddHours(7.5), MidnightBoston.AddHours(9.5));
        await AddPositionAsync("observed", MidnightBoston.AddHours(8));

        await RecordAsync();
        await AddPositionAsync("missing", MidnightBoston.AddHours(9));
        var second = await RecordAsync();

        Assert.Equal(2, second?.TripsObserved);

        await using var context = database.CreateContext();
        Assert.Equal(5, await context.ScheduledTripOutcomes.CountAsync());
        Assert.Equal(2, (await context.TripOutcomeRuns.SingleAsync()).TripsObserved);
        Assert.Equal(
            [Monday],
            await new TripOutcomeRecorder(context, new FakeTimeProvider())
                .GetRecordedServiceDatesAsync(Monday.AddDays(-1), Monday.AddDays(1), CancellationToken.None));
    }

    private async Task<TripOutcomeRunResult?> RecordAsync()
    {
        await using var context = database.CreateContext();
        var recorder = new TripOutcomeRecorder(context, new FakeTimeProvider(MidnightBoston.AddDays(2)));
        return await recorder.RecordAsync(Monday, MaxFeedGap, CancellationToken.None);
    }

    private async Task<int> AddFeedAsync(string sha256, DateTimeOffset importedAt)
    {
        await using var context = database.CreateContext();

        var feed = new GtfsFeed
        {
            Sha256 = sha256,
            SourceUrl = "https://feeds.example.test/gtfs.zip",
            StartDate = Monday.AddDays(-30),
            EndDate = Monday.AddDays(30),
            ImportedAt = importedAt,
        };
        context.GtfsFeeds.Add(feed);
        await context.SaveChangesAsync();

        context.Agencies.Add(new Agency { FeedId = feed.Id, Name = "Example Transit", TimeZone = "America/New_York" });
        context.ServiceCalendars.AddRange(
            Calendar(feed.Id, "weekday", weekdays: true),
            Calendar(feed.Id, "weekday-except-today", weekdays: true),
            Calendar(feed.Id, "weekend", weekdays: false));
        context.CalendarDates.AddRange(
            new CalendarDate { FeedId = feed.Id, ServiceId = "special", Date = Monday, ExceptionType = CalendarExceptionType.Added },
            new CalendarDate { FeedId = feed.Id, ServiceId = "weekday-except-today", Date = Monday, ExceptionType = CalendarExceptionType.Removed });

        foreach (var trip in Trips)
        {
            context.Trips.Add(new Trip { FeedId = feed.Id, TripId = trip.TripId, RouteId = trip.RouteId, ServiceId = trip.ServiceId, DirectionId = 0 });
            context.StopTimes.AddRange(
                new StopTime { FeedId = feed.Id, TripId = trip.TripId, StopSequence = 1, StopId = "A", DepartureTime = GtfsTime.Parse(trip.Start).SinceServiceDayStart },
                new StopTime { FeedId = feed.Id, TripId = trip.TripId, StopSequence = 2, StopId = "B", ArrivalTime = GtfsTime.Parse(trip.End).SinceServiceDayStart });
        }

        await context.SaveChangesAsync();
        return feed.Id;
    }

    private async Task AddPollsAsync(DateTimeOffset from, DateTimeOffset to)
    {
        await using var context = database.CreateContext();
        for (var at = from; at <= to; at = at.AddMinutes(1))
        {
            context.FeedPolls.Add(new FeedPoll { PolledAt = at, FeedTimestamp = at, EntityCount = 1 });
        }

        await context.SaveChangesAsync();
    }

    private async Task AddPositionAsync(string tripId, DateTimeOffset observedAt)
    {
        await using var context = database.CreateContext();
        await new VehiclePositionWriter(context).InsertNewAsync(
            [
                new VehiclePosition
                {
                    VehicleId = "y1",
                    TripId = tripId,
                    RouteId = "42",
                    StartDate = Monday,
                    Latitude = 42.35,
                    Longitude = -71.05,
                    ObservedAt = observedAt,
                },
            ],
            CancellationToken.None);
    }

    private static ServiceCalendar Calendar(int feedId, string serviceId, bool weekdays) =>
        new()
        {
            FeedId = feedId,
            ServiceId = serviceId,
            Monday = weekdays,
            Tuesday = weekdays,
            Wednesday = weekdays,
            Thursday = weekdays,
            Friday = weekdays,
            Saturday = !weekdays,
            Sunday = !weekdays,
            StartDate = Monday.AddDays(-30),
            EndDate = Monday.AddDays(30),
        };

    private sealed record ScheduledTripSpec(string TripId, string RouteId, string ServiceId, string Start, string End);
}
