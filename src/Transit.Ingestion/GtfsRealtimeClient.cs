using Microsoft.Extensions.Options;
using TransitRealtime;

namespace Transit.Ingestion;

public class GtfsRealtimeClient(HttpClient httpClient, IOptions<GtfsRealtimeOptions> options)
{
    public async Task<FeedMessage> GetVehiclePositionsAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(options.Value.VehiclePositionsUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return FeedMessage.Parser.ParseFrom(body);
    }
}
