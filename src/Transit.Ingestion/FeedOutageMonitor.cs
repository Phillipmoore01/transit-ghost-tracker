using System.Data.Common;
using Microsoft.Extensions.Options;
using Transit.Data;

namespace Transit.Ingestion;

public class FeedOutageMonitor(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<FeedHealthOptions> options,
    ILogger<FeedOutageMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CheckInterval, timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CheckOnceAsync(stoppingToken);
        }
    }

    private async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var recorder = scope.ServiceProvider.GetRequiredService<FeedOutageRecorder>();
            var alertSender = scope.ServiceProvider.GetRequiredService<OutageAlertSender>();
            var events = await recorder.DetectAsync(settings.Lookback, settings.MaxFeedGap, cancellationToken);

            foreach (var outage in events)
            {
                await alertSender.SendAsync(outage, cancellationToken);
            }
        }
        catch (Exception ex) when (
            (ex is DbException or TimeoutException) && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Feed outage check failed; retrying on the next interval");
        }
    }
}
