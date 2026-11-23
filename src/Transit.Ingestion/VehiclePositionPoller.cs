using System.Data.Common;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using Transit.Data;
using Transit.Gtfs;

namespace Transit.Ingestion;

public class VehiclePositionPoller(
    GtfsRealtimeClient client,
    IServiceScopeFactory scopeFactory,
    IOptions<GtfsRealtimeOptions> options,
    ILogger<VehiclePositionPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval);

        do
        {
            await PollOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var feed = await client.GetVehiclePositionsAsync(cancellationToken);
            var positions = VehiclePositionReader.Read(feed).Select(ToEntity).ToList();

            await using var scope = scopeFactory.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<VehiclePositionWriter>();
            var inserted = await writer.InsertNewAsync(positions, cancellationToken);

            logger.LogInformation(
                "Feed at {FeedTimestamp:O}: {EntityCount} entities, {ObservationCount} usable vehicle positions, {InsertedCount} new",
                DateTimeOffset.FromUnixTimeSeconds((long)feed.Header.Timestamp),
                feed.Entity.Count,
                positions.Count,
                inserted);
        }
        catch (Exception ex) when (
            (ex is HttpRequestException or InvalidProtocolBufferException or TaskCanceledException or DbException)
            && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Vehicle positions poll failed; retrying on the next interval");
        }
    }

    private static VehiclePosition ToEntity(VehicleObservation observation) =>
        new()
        {
            VehicleId = observation.VehicleId,
            TripId = observation.TripId,
            RouteId = observation.RouteId,
            StartDate = observation.StartDate,
            Latitude = observation.Latitude,
            Longitude = observation.Longitude,
            ObservedAt = observation.ObservedAt,
            StopId = observation.StopId,
            CurrentStopSequence = observation.CurrentStopSequence,
            CurrentStatus = observation.CurrentStatus,
        };
}
