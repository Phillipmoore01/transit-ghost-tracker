using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Transit.Data;

namespace Transit.Ingestion.Tests;

public class OutageAlertSenderTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Describes_a_started_outage()
    {
        var message = OutageAlertSender.Describe(new FeedOutageEvent(FeedOutageChange.Started, Start, null));

        Assert.Equal("Vehicle positions feed has not updated since 2026-06-01 12:00:00 UTC.", message);
    }

    [Fact]
    public void Describes_an_ended_outage_with_its_duration()
    {
        var message = OutageAlertSender.Describe(
            new FeedOutageEvent(FeedOutageChange.FoundAfterItEnded, Start, Start.AddMinutes(75).AddSeconds(5)));

        Assert.Equal(
            "Vehicle positions feed resumed at 2026-06-01 13:15:05 UTC after 1 h 15 min without updates (since 2026-06-01 12:00:00 UTC); the gap was found after it ended.",
            message);
    }

    [Fact]
    public async Task Posts_the_message_to_the_configured_webhook()
    {
        var handler = new RecordingHandler();
        var sender = new OutageAlertSender(
            new HttpClient(handler),
            Options.Create(new FeedHealthOptions { AlertWebhookUrl = new Uri("https://hooks.example.test/alerts") }),
            NullLogger<OutageAlertSender>.Instance);

        await sender.SendAsync(new FeedOutageEvent(FeedOutageChange.Started, Start, null), CancellationToken.None);

        Assert.Equal(new Uri("https://hooks.example.test/alerts"), handler.RequestUri);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(
            "Vehicle positions feed has not updated since 2026-06-01 12:00:00 UTC.",
            body.RootElement.GetProperty("text").GetString());
        Assert.Equal(
            body.RootElement.GetProperty("text").GetString(),
            body.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Does_not_post_without_a_webhook()
    {
        var handler = new RecordingHandler();
        var sender = new OutageAlertSender(
            new HttpClient(handler),
            Options.Create(new FeedHealthOptions()),
            NullLogger<OutageAlertSender>.Instance);

        await sender.SendAsync(new FeedOutageEvent(FeedOutageChange.Started, Start, null), CancellationToken.None);

        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Does_not_throw_when_the_webhook_fails()
    {
        var handler = new RecordingHandler(HttpStatusCode.InternalServerError);
        var sender = new OutageAlertSender(
            new HttpClient(handler),
            Options.Create(new FeedHealthOptions { AlertWebhookUrl = new Uri("https://hooks.example.test/alerts") }),
            NullLogger<OutageAlertSender>.Instance);

        await sender.SendAsync(new FeedOutageEvent(FeedOutageChange.Started, Start, null), CancellationToken.None);

        Assert.NotNull(handler.RequestUri);
    }

    private sealed class RecordingHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }
}
