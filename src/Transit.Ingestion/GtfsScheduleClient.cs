using Microsoft.Extensions.Options;

namespace Transit.Ingestion;

public class GtfsScheduleClient(HttpClient httpClient, IOptions<GtfsStaticOptions> options)
{
    public async Task DownloadScheduleAsync(Stream destination, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            options.Value.ScheduleUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await body.CopyToAsync(destination, cancellationToken);
    }
}
