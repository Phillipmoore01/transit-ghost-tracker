using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Transit.Data.Tests;

public sealed class FeedOutageRecorderTests(PostgresFixture database) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lookback = TimeSpan.FromDays(1);
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(2);

    public async Task InitializeAsync()
    {
        await using var context = database.CreateContext();
        await context.FeedOutages.ExecuteDeleteAsync();
        await context.FeedPolls.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Reports_an_outage_once_when_it_starts_and_once_when_it_ends()
    {
        await AddPollsAsync(0, 20, 40);

        var started = await DetectAsync(nowSeconds: 300);
        var stillDown = await DetectAsync(nowSeconds: 330);
        await AddPollsAsync(600);
        var ended = await DetectAsync(nowSeconds: 610);
        var afterwards = await DetectAsync(nowSeconds: 640);

        Assert.Equal([new FeedOutageEvent(FeedOutageChange.Started, Start.AddSeconds(40), null)], started);
        Assert.Empty(stillDown);
        Assert.Equal([new FeedOutageEvent(FeedOutageChange.Ended, Start.AddSeconds(40), Start.AddSeconds(600))], ended);
        Assert.Empty(afterwards);

        await using var context = database.CreateContext();
        var outage = await context.FeedOutages.SingleAsync();
        Assert.Equal(Start.AddSeconds(40), outage.StartedAt);
        Assert.Equal(Start.AddSeconds(600), outage.EndedAt);
        Assert.Equal(Start.AddSeconds(300), outage.DetectedAt);
    }

    [Fact]
    public async Task Reports_a_gap_found_after_it_ended()
    {
        await AddPollsAsync(0, 20, 900, 920);

        var events = await DetectAsync(nowSeconds: 930);

        Assert.Equal([new FeedOutageEvent(FeedOutageChange.FoundAfterItEnded, Start.AddSeconds(20), Start.AddSeconds(900))], events);
    }

    [Fact]
    public async Task Finds_an_outage_that_began_before_the_lookback_window()
    {
        await AddPollsAsync(0);

        var events = await DetectAsync(nowSeconds: (int)TimeSpan.FromDays(3).TotalSeconds);

        Assert.Equal([new FeedOutageEvent(FeedOutageChange.Started, Start, null)], events);
    }

    [Fact]
    public async Task Returns_the_latest_feed_timestamp()
    {
        await using var context = database.CreateContext();
        var recorder = new FeedOutageRecorder(context, new FakeTimeProvider(Start));

        Assert.Null(await recorder.GetLatestFeedTimestampAsync(CancellationToken.None));

        await AddPollsAsync(0, 40, 20);

        Assert.Equal(Start.AddSeconds(40), await recorder.GetLatestFeedTimestampAsync(CancellationToken.None));
    }

    private async Task<IReadOnlyList<FeedOutageEvent>> DetectAsync(int nowSeconds)
    {
        await using var context = database.CreateContext();
        var recorder = new FeedOutageRecorder(context, new FakeTimeProvider(Start.AddSeconds(nowSeconds)));
        return await recorder.DetectAsync(Lookback, MaxGap, CancellationToken.None);
    }

    private async Task AddPollsAsync(params int[] secondsAfterStart)
    {
        await using var context = database.CreateContext();
        context.FeedPolls.AddRange(secondsAfterStart.Select(s => new FeedPoll
        {
            PolledAt = Start.AddSeconds(s),
            FeedTimestamp = Start.AddSeconds(s),
            EntityCount = 1,
        }));
        await context.SaveChangesAsync();
    }
}
