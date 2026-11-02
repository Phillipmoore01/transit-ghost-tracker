using Microsoft.EntityFrameworkCore;

namespace Transit.Data.Tests;

public sealed class VehiclePositionWriterTests(PostgresFixture database) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.FromUnixTimeSeconds(1_780_000_000);

    public async Task InitializeAsync()
    {
        await using var context = database.CreateContext();
        await context.VehiclePositions.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Writing_the_same_snapshot_twice_stores_each_position_once()
    {
        var snapshot = new[] { Position("y1", ObservedAt), Position("y2", ObservedAt) };

        var firstInserted = await InsertAsync(snapshot);
        var secondInserted = await InsertAsync(snapshot);

        Assert.Equal(2, firstInserted);
        Assert.Equal(0, secondInserted);
        Assert.Equal(2, await CountAsync());
    }

    [Fact]
    public async Task Stores_only_the_positions_not_already_recorded()
    {
        await InsertAsync([Position("y1", ObservedAt)]);

        var inserted = await InsertAsync([Position("y1", ObservedAt), Position("y1", ObservedAt.AddSeconds(20))]);

        Assert.Equal(1, inserted);
        Assert.Equal(2, await CountAsync());
    }

    [Fact]
    public async Task Ignores_duplicates_within_a_single_batch()
    {
        var inserted = await InsertAsync([Position("y1", ObservedAt), Position("y1", ObservedAt)]);

        Assert.Equal(1, inserted);
    }

    [Fact]
    public async Task Round_trips_every_field_including_missing_optional_values()
    {
        var complete = Position("y1", ObservedAt);
        var sparse = Position("y2", ObservedAt);
        sparse.RouteId = null;
        sparse.StartDate = null;
        sparse.StopId = null;

        await InsertAsync([complete, sparse]);

        await using var context = database.CreateContext();
        var stored = await context.VehiclePositions.AsNoTracking().OrderBy(p => p.VehicleId).ToListAsync();

        Assert.Collection(
            stored,
            p => AssertSameFields(complete, p),
            p => AssertSameFields(sparse, p));
    }

    [Fact]
    public async Task Writing_an_empty_batch_inserts_nothing()
    {
        Assert.Equal(0, await InsertAsync([]));
    }

    private async Task<int> InsertAsync(IReadOnlyCollection<VehiclePosition> positions)
    {
        await using var context = database.CreateContext();
        return await new VehiclePositionWriter(context).InsertNewAsync(positions, CancellationToken.None);
    }

    private async Task<int> CountAsync()
    {
        await using var context = database.CreateContext();
        return await context.VehiclePositions.CountAsync();
    }

    private static VehiclePosition Position(string vehicleId, DateTimeOffset observedAt) =>
        new()
        {
            VehicleId = vehicleId,
            TripId = "trip-42",
            RouteId = "42",
            StartDate = new DateOnly(2026, 6, 1),
            Latitude = 42.35,
            Longitude = -71.05,
            ObservedAt = observedAt,
            StopId = "place-sstat",
        };

    private static void AssertSameFields(VehiclePosition expected, VehiclePosition actual)
    {
        Assert.Equal(expected.VehicleId, actual.VehicleId);
        Assert.Equal(expected.TripId, actual.TripId);
        Assert.Equal(expected.RouteId, actual.RouteId);
        Assert.Equal(expected.StartDate, actual.StartDate);
        Assert.Equal(expected.Latitude, actual.Latitude);
        Assert.Equal(expected.Longitude, actual.Longitude);
        Assert.Equal(expected.ObservedAt, actual.ObservedAt);
        Assert.Equal(expected.StopId, actual.StopId);
    }
}
