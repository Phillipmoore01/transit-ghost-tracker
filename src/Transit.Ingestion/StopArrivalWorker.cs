using System.Data.Common;
using Microsoft.Extensions.Options;
using Transit.Data;

namespace Transit.Ingestion;

public class StopArrivalWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<StopArrivalOptions> options,
    ILogger<StopArrivalWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.RunInterval, timeProvider);

        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var recorder = scope.ServiceProvider.GetRequiredService<StopArrivalRecorder>();

            var timeZoneId = await recorder.GetLatestAgencyTimeZoneAsync(cancellationToken);
            if (timeZoneId is null || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone))
            {
                logger.LogInformation("No static schedule with a known agency time zone yet; skipping stop arrival inference");
                return;
            }

            var completed = CompletedServiceDates.Within(
                timeProvider.GetUtcNow(),
                timeZone,
                settings.ServiceDayLength,
                settings.LookbackDays);
            if (completed.Count == 0)
            {
                return;
            }

            var recorded = await recorder.GetRecordedServiceDatesAsync(completed[0], completed[^1], cancellationToken);

            foreach (var serviceDate in completed.Where(d => !recorded.Contains(d)))
            {
                var result = await recorder.RecordAsync(serviceDate, settings.MaxInterpolationGap, cancellationToken);

                logger.LogInformation(
                    "Stop arrivals for service date {ServiceDate:yyyy-MM-dd}: {TripsObserved} trips observed, {TripsMatched} matched to the schedule, {ArrivalsRecorded} arrivals recorded",
                    result.ServiceDate,
                    result.TripsObserved,
                    result.TripsMatched,
                    result.ArrivalsRecorded);
            }
        }
        catch (Exception ex) when (
            (ex is DbException or TimeoutException) && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Stop arrival inference failed; retrying on the next interval");
        }
    }
}
