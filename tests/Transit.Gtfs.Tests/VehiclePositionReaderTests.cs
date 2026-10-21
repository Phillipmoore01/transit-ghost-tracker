using TransitRealtime;

namespace Transit.Gtfs.Tests;

public class VehiclePositionReaderTests
{
    private const ulong ObservedAtUnixSeconds = 1_780_000_000;

    [Fact]
    public void Reads_a_complete_vehicle_position()
    {
        var feed = Feed(Entity("1", Vehicle("y1234", "trip-42", routeId: "42", startDate: "20260601", stopId: "place-sstat")));

        var observation = Assert.Single(VehiclePositionReader.Read(feed));

        Assert.Equal(
            new VehicleObservation(
                "y1234",
                "trip-42",
                "42",
                new DateOnly(2026, 6, 1),
                (double)42.35f,
                (double)-71.05f,
                DateTimeOffset.FromUnixTimeSeconds((long)ObservedAtUnixSeconds),
                "place-sstat"),
            observation);
    }

    [Fact]
    public void Treats_empty_optional_fields_as_missing()
    {
        var feed = Feed(Entity("1", Vehicle("y1234", "trip-42", routeId: "", startDate: "", stopId: "")));

        var observation = Assert.Single(VehiclePositionReader.Read(feed));

        Assert.Null(observation.RouteId);
        Assert.Null(observation.StartDate);
        Assert.Null(observation.StopId);
    }

    [Fact]
    public void Skips_entities_that_cannot_be_matched_to_a_trip()
    {
        var withoutTrip = Vehicle("y1", "trip-1");
        withoutTrip.Trip = null;

        var withoutPosition = Vehicle("y2", "trip-2");
        withoutPosition.Position = null;

        var withoutTimestamp = Vehicle("y3", "trip-3");
        withoutTimestamp.ClearTimestamp();

        var deleted = Entity("4", Vehicle("y4", "trip-4"));
        deleted.IsDeleted = true;

        var feed = Feed(
            Entity("1", withoutTrip),
            Entity("2", withoutPosition),
            Entity("3", withoutTimestamp),
            deleted,
            new FeedEntity { Id = "5" },
            Entity("6", Vehicle("y6", "trip-6")));

        var observation = Assert.Single(VehiclePositionReader.Read(feed));

        Assert.Equal("y6", observation.VehicleId);
    }

    private static FeedMessage Feed(params FeedEntity[] entities)
    {
        var feed = new FeedMessage { Header = new FeedHeader { GtfsRealtimeVersion = "2.0" } };
        feed.Entity.AddRange(entities);
        return feed;
    }

    private static FeedEntity Entity(string id, TransitRealtime.VehiclePosition vehicle) =>
        new() { Id = id, Vehicle = vehicle };

    private static TransitRealtime.VehiclePosition Vehicle(
        string vehicleId,
        string tripId,
        string routeId = "1",
        string startDate = "20260601",
        string stopId = "1") =>
        new()
        {
            Vehicle = new VehicleDescriptor { Id = vehicleId },
            Trip = new TripDescriptor { TripId = tripId, RouteId = routeId, StartDate = startDate },
            Position = new Position { Latitude = 42.35f, Longitude = -71.05f },
            Timestamp = ObservedAtUnixSeconds,
            StopId = stopId,
        };
}
