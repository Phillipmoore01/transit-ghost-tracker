using System.Net;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using TransitRealtime;

namespace Transit.Ingestion.Tests;

public class GtfsRealtimeClientTests
{
    private static readonly Uri FeedUrl = new("https://feeds.example.test/VehiclePositions.pb");

    [Fact]
    public async Task Downloads_and_parses_the_vehicle_positions_feed()
    {
        var expected = new FeedMessage { Header = new FeedHeader { GtfsRealtimeVersion = "2.0", Timestamp = 1_780_000_000 } };
        expected.Entity.Add(new FeedEntity { Id = "1" });
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(expected.ToByteArray()) });

        var feed = await client.GetVehiclePositionsAsync(CancellationToken.None);

        Assert.Equal(expected, feed);
    }

    [Fact]
    public async Task Fails_on_an_unsuccessful_response()
    {
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetVehiclePositionsAsync(CancellationToken.None));
    }

    private static GtfsRealtimeClient CreateClient(HttpResponseMessage response)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri == FeedUrl),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return new GtfsRealtimeClient(
            new HttpClient(handler.Object),
            Options.Create(new GtfsRealtimeOptions { VehiclePositionsUrl = FeedUrl }));
    }
}
