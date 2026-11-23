using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Transit.Data;

namespace Transit.Ingestion;

public class OutageAlertSender(
    HttpClient httpClient,
    IOptions<FeedHealthOptions> options,
    ILogger<OutageAlertSender> logger)
{
    public async Task SendAsync(FeedOutageEvent outage, CancellationToken cancellationToken)
    {
        var message = Describe(outage);

        if (outage.Change == FeedOutageChange.Started)
        {
            logger.LogWarning("{AlertMessage}", message);
        }
        else
        {
            logger.LogInformation("{AlertMessage}", message);
        }

        if (options.Value.AlertWebhookUrl is not { IsAbsoluteUri: true } webhookUrl)
        {
            return;
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                webhookUrl,
                new { text = message, content = message },
                cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Outage alert webhook failed");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Outage alert webhook timed out");
        }
    }

    public static string Describe(FeedOutageEvent outage) =>
        outage switch
        {
            { Change: FeedOutageChange.Started } =>
                $"Vehicle positions feed has not updated since {Format(outage.StartedAt)}.",
            { EndedAt: { } endedAt } =>
                $"Vehicle positions feed resumed at {Format(endedAt)} after {FormatDuration(endedAt - outage.StartedAt)} without updates (since {Format(outage.StartedAt)})"
                + (outage.Change == FeedOutageChange.FoundAfterItEnded ? "; the gap was found after it ended." : "."),
            _ => $"Vehicle positions feed outage since {Format(outage.StartedAt)} changed.",
        };

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours} h {duration.Minutes} min"
            : $"{(int)duration.TotalMinutes} min {duration.Seconds} s";
}
