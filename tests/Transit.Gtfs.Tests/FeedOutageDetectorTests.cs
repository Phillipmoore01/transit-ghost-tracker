using Transit.Gtfs.Reliability;

namespace Transit.Gtfs.Tests;

public class FeedOutageDetectorTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(2);

    [Fact]
    public void Finds_no_outage_while_the_feed_keeps_updating()
    {
        Assert.Empty(Find([0, 20, 40, 60], nowSeconds: 80));
    }

    [Fact]
    public void Reports_a_gap_between_two_feed_timestamps_as_an_ended_outage()
    {
        var outage = Assert.Single(Find([0, 20, 600, 620], nowSeconds: 630));

        Assert.Equal(new FeedOutagePeriod(Start.AddSeconds(20), Start.AddSeconds(600)), outage);
    }

    [Fact]
    public void Reports_a_stale_feed_as_an_ongoing_outage()
    {
        var outage = Assert.Single(Find([0, 20], nowSeconds: 200));

        Assert.Equal(new FeedOutagePeriod(Start.AddSeconds(20), null), outage);
    }

    [Fact]
    public void Treats_a_repeated_feed_timestamp_as_no_new_data()
    {
        var outage = Assert.Single(Find([0, 20, 20, 20, 20], nowSeconds: 200));

        Assert.Equal(Start.AddSeconds(20), outage.LastFeedBefore);
    }

    [Fact]
    public void Finds_nothing_without_any_feed_data()
    {
        Assert.Empty(Find([], nowSeconds: 1000));
    }

    private static IReadOnlyList<FeedOutagePeriod> Find(int[] secondsAfterStart, int nowSeconds) =>
        FeedOutageDetector.Find(secondsAfterStart.Select(s => Start.AddSeconds(s)), Start.AddSeconds(nowSeconds), MaxGap);
}
