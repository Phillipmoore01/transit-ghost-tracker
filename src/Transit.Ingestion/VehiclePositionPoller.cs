using Google.Protobuf;
using Microsoft.Extensions.Options;
using Transit.Gtfs;

namespace Transit.Ingestion;

public class VehiclePositionPoller(
    GtfsRealtimeClient client,
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
            var observations = VehiclePositionReader.Read(feed).ToList();

            logger.LogInformation(
                "Feed at {FeedTimestamp:O}: {EntityCount} entities, {ObservationCount} usable vehicle positions",
                DateTimeOffset.FromUnixTimeSeconds((long)feed.Header.Timestamp),
                feed.Entity.Count,
                observations.Count);
        }
        catch (Exception ex) when (
            (ex is HttpRequestException or InvalidProtocolBufferException or TaskCanceledException)
            && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Vehicle positions poll failed; retrying on the next interval");
        }
    }
}
