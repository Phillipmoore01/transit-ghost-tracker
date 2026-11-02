using System.Data.Common;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Transit.Data;
using Transit.Gtfs.Static;

namespace Transit.Ingestion;

public class StaticScheduleRefresher(
    GtfsScheduleClient client,
    IServiceScopeFactory scopeFactory,
    IOptions<GtfsStaticOptions> options,
    ILogger<StaticScheduleRefresher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.RefreshInterval);

        do
        {
            await RefreshOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        var sourceUrl = options.Value.ScheduleUrl;

        try
        {
            var download = new FileStream(
                Path.GetTempFileName(),
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);

            await using (download)
            {
                await client.DownloadScheduleAsync(download, cancellationToken);

                download.Position = 0;
                var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(download, cancellationToken));
                download.Position = 0;

                await using var scope = scopeFactory.CreateAsyncScope();
                var importer = scope.ServiceProvider.GetRequiredService<GtfsFeedImporter>();

                if (await importer.IsImportedAsync(sha256, cancellationToken))
                {
                    logger.LogInformation("Static schedule from {SourceUrl} is unchanged ({Sha256})", sourceUrl, sha256);
                    return;
                }

                using var feed = new GtfsStaticFeed(new ZipArchive(download, ZipArchiveMode.Read, leaveOpen: true));
                var result = await importer.ImportAsync(feed, sha256, sourceUrl, cancellationToken);

                logger.LogInformation(
                    "Imported static schedule {FeedVersion} as feed {FeedId}: {Routes} routes, {Stops} stops, {Trips} trips, {StopTimes} stop times, {ServiceCalendars} calendars, {CalendarDates} calendar dates",
                    result.FeedVersion,
                    result.FeedId,
                    result.Routes,
                    result.Stops,
                    result.Trips,
                    result.StopTimes,
                    result.ServiceCalendars,
                    result.CalendarDates);
            }
        }
        catch (Exception ex) when (
            (ex is HttpRequestException or TaskCanceledException or InvalidDataException or FormatException or DbException or IOException)
            && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Static schedule refresh from {SourceUrl} failed; retrying on the next interval", sourceUrl);
        }
    }
}
