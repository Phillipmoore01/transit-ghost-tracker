using System.Data.Common;
using Microsoft.Extensions.Options;
using Transit.Data;

namespace Transit.Ingestion;

public class ServiceDayWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<ServiceDayOptions> options,
    ILogger<ServiceDayWorker> logger) : BackgroundService
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
            var arrivals = scope.ServiceProvider.GetRequiredService<StopArrivalRecorder>();
            var outcomes = scope.ServiceProvider.GetRequiredService<TripOutcomeRecorder>();

            var timeZoneId = await arrivals.GetLatestAgencyTimeZoneAsync(cancellationToken);
            if (timeZoneId is null || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone))
            {
                logger.LogInformation("No static schedule with a known agency time zone yet; skipping service day processing");
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

            var arrivalsRecorded = await arrivals.GetRecordedServiceDatesAsync(completed[0], completed[^1], cancellationToken);
            var outcomesRecorded = await outcomes.GetRecordedServiceDatesAsync(completed[0], completed[^1], cancellationToken);

            foreach (var serviceDate in completed)
            {
                if (!arrivalsRecorded.Contains(serviceDate))
                {
                    await RecordArrivalsAsync(arrivals, serviceDate, settings, cancellationToken);
                }

                if (!outcomesRecorded.Contains(serviceDate))
                {
                    await RecordOutcomesAsync(outcomes, serviceDate, settings, cancellationToken);
                }
            }
        }
        catch (Exception ex) when (
            (ex is DbException or TimeoutException) && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Service day processing failed; retrying on the next interval");
        }
    }

    private async Task RecordArrivalsAsync(
        StopArrivalRecorder recorder,
        DateOnly serviceDate,
        ServiceDayOptions settings,
        CancellationToken cancellationToken)
    {
        var result = await recorder.RecordAsync(serviceDate, settings.MaxInterpolationGap, cancellationToken);

        logger.LogInformation(
            "Stop arrivals for service date {ServiceDate:yyyy-MM-dd}: {TripsObserved} trips observed, {TripsMatched} matched to the schedule, {ArrivalsRecorded} arrivals recorded",
            result.ServiceDate,
            result.TripsObserved,
            result.TripsMatched,
            result.ArrivalsRecorded);
    }

    private async Task RecordOutcomesAsync(
        TripOutcomeRecorder recorder,
        DateOnly serviceDate,
        ServiceDayOptions settings,
        CancellationToken cancellationToken)
    {
        if (await recorder.RecordAsync(serviceDate, settings.MaxFeedGap, cancellationToken) is not { } result)
        {
            logger.LogWarning("No static schedule covers service date {ServiceDate:yyyy-MM-dd}; trip outcomes not recorded", serviceDate);
            return;
        }

        logger.LogInformation(
            "Trip outcomes for service date {ServiceDate:yyyy-MM-dd} (feed {FeedId}): {TripsScheduled} scheduled, {TripsObserved} observed, {TripsNotObserved} not observed, {TripsInFeedGaps} during feed gaps, {TripsOnUntrackedRoutes} on untracked routes",
            result.ServiceDate,
            result.FeedId,
            result.TripsScheduled,
            result.TripsObserved,
            result.TripsNotObserved,
            result.TripsInFeedGaps,
            result.TripsOnUntrackedRoutes);
    }
}
