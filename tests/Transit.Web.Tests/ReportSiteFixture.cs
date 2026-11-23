using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Transit.Data;
using Transit.Gtfs.Arrivals;
using Transit.Gtfs.Reliability;

namespace Transit.Web.Tests;

public sealed class ReportSiteFixture : IAsyncLifetime
{
    public static readonly DateOnly ServiceDate = new(2026, 6, 1);

    private static readonly DateTimeOffset MidnightBoston = new(2026, 6, 1, 4, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17").Build();

    private WebApplicationFactory<Program>? factory;

    public HttpClient CreateClient() =>
        factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async Task InitializeAsync()
    {
        await container.StartAsync();

        await using (var context = new TransitDbContext(
            new DbContextOptionsBuilder<TransitDbContext>().UseNpgsql(container.GetConnectionString()).Options))
        {
            await context.Database.MigrateAsync();
            await SeedAsync(context);
        }

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Transit", container.GetConnectionString());
        });
    }

    public async Task DisposeAsync()
    {
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }

        await container.DisposeAsync();
    }

    private static async Task SeedAsync(TransitDbContext context)
    {
        var feed = new GtfsFeed { Sha256 = "hash-1", SourceUrl = "https://feeds.example.test/gtfs.zip", ImportedAt = MidnightBoston.AddDays(-1) };
        context.GtfsFeeds.Add(feed);
        await context.SaveChangesAsync();

        context.Agencies.Add(new Agency { FeedId = feed.Id, Name = "Example Transit", TimeZone = "America/New_York" });
        context.Routes.AddRange(
            new Route { FeedId = feed.Id, RouteId = "42", ShortName = "42", LongName = "Forest Hills - Dudley", RouteType = 3 },
            new Route { FeedId = feed.Id, RouteId = "Red", LongName = "Red Line", RouteType = 1 });
        context.Stops.AddRange(
            new Stop { FeedId = feed.Id, StopId = "A", Name = "Alpha St", LocationType = 0 },
            new Stop { FeedId = feed.Id, StopId = "B", Name = "Beta Sq", LocationType = 0 },
            new Stop { FeedId = feed.Id, StopId = "place-x", Name = "Central Station", LocationType = 1 },
            new Stop { FeedId = feed.Id, StopId = "70080", Name = "Central Station - Red Line", LocationType = 0, ParentStation = "place-x" });
        context.Trips.Add(new Trip { FeedId = feed.Id, TripId = "t2", RouteId = "42", ServiceId = "weekday", Headsign = "Dudley" });

        context.ScheduledTripOutcomes.AddRange(
            Outcome(feed.Id, "t1", "42", 8, 0, TripOutcome.Observed),
            Outcome(feed.Id, "t2", "42", 8, 30, TripOutcome.NotObserved),
            Outcome(feed.Id, "t3", "42", 12, 0, TripOutcome.Observed),
            Outcome(feed.Id, "t4", "42", 20, 0, TripOutcome.FeedGap),
            Outcome(feed.Id, "t5", "Red", 7, 0, TripOutcome.Observed));

        context.ObservedStopArrivals.AddRange(
            Arrival(feed.Id, "t1", "42", "B", 2, delaySeconds: 30),
            Arrival(feed.Id, "t3", "42", "B", 2, delaySeconds: 400),
            Arrival(feed.Id, "t1", "42", "A", 3, delaySeconds: -90),
            Arrival(feed.Id, "t5", "Red", "70080", 2, delaySeconds: 120));

        context.TripOutcomeRuns.Add(new TripOutcomeRun { ServiceDate = ServiceDate, FeedId = feed.Id, CompletedAt = MidnightBoston.AddDays(2) });
        await context.SaveChangesAsync();
    }

    private static ScheduledTripOutcome Outcome(int feedId, string tripId, string routeId, int hour, int minute, TripOutcome outcome)
    {
        var start = MidnightBoston.AddHours(hour).AddMinutes(minute);
        return new ScheduledTripOutcome
        {
            ServiceDate = ServiceDate,
            TripId = tripId,
            FeedId = feedId,
            RouteId = routeId,
            DirectionId = 0,
            ScheduledStart = start,
            ScheduledEnd = start.AddMinutes(30),
            Outcome = outcome,
            FirstObservedAt = outcome == TripOutcome.Observed ? start : null,
        };
    }

    private static ObservedStopArrival Arrival(int feedId, string tripId, string routeId, string stopId, int sequence, int delaySeconds)
    {
        var scheduled = MidnightBoston.AddHours(9).AddMinutes(sequence);
        return new ObservedStopArrival
        {
            TripId = tripId,
            ServiceDate = ServiceDate,
            StopSequence = sequence,
            FeedId = feedId,
            RouteId = routeId,
            StopId = stopId,
            VehicleId = "y1",
            ScheduledAt = scheduled,
            ObservedAt = scheduled.AddSeconds(delaySeconds),
            Evidence = ArrivalEvidence.ReportedStopped,
        };
    }
}
