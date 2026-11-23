using Transit.Gtfs.Reliability;

namespace Transit.Gtfs.Tests;

public class FeedCoverageTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(2);

    [Fact]
    public void Covers_a_window_polled_throughout()
    {
        var coverage = Coverage(Enumerable.Range(0, 60).Select(i => i * 20));

        Assert.True(coverage.Covers(Start.AddSeconds(30), Start.AddSeconds(1000)));
    }

    [Fact]
    public void Does_not_cover_a_window_containing_a_gap_longer_than_the_limit()
    {
        var coverage = Coverage([0, 20, 40, 400, 420]);

        Assert.False(coverage.Covers(Start.AddSeconds(10), Start.AddSeconds(410)));
        Assert.True(coverage.Covers(Start.AddSeconds(400), Start.AddSeconds(420)));
    }

    [Fact]
    public void Requires_feed_data_close_to_both_ends_of_the_window()
    {
        var coverage = Coverage([300, 320, 340]);

        Assert.False(coverage.Covers(Start, Start.AddSeconds(340)));
        Assert.False(coverage.Covers(Start.AddSeconds(300), Start.AddSeconds(600)));
        Assert.True(coverage.Covers(Start.AddSeconds(200), Start.AddSeconds(440)));
    }

    [Fact]
    public void Covers_a_short_window_between_two_close_timestamps()
    {
        var coverage = Coverage([0, 60]);

        Assert.True(coverage.Covers(Start.AddSeconds(10), Start.AddSeconds(50)));
        Assert.False(Coverage([]).Covers(Start, Start.AddMinutes(10)));
    }

    [Fact]
    public void Ignores_duplicate_and_unordered_timestamps()
    {
        var coverage = Coverage([100, 0, 50, 50, 150]);

        Assert.True(coverage.Covers(Start, Start.AddSeconds(150)));
    }

    [Theory]
    [InlineData(true, true, true, TripOutcome.Observed)]
    [InlineData(true, false, false, TripOutcome.Observed)]
    [InlineData(false, false, true, TripOutcome.RouteNotTracked)]
    [InlineData(false, true, false, TripOutcome.FeedGap)]
    [InlineData(false, true, true, TripOutcome.NotObserved)]
    public void Classifies_a_trip_as_not_observed_only_when_its_route_and_window_were_tracked(
        bool observed,
        bool routeTracked,
        bool covered,
        TripOutcome expected)
    {
        Assert.Equal(expected, TripOutcomeClassifier.Classify(observed, routeTracked, covered));
    }

    private static FeedCoverage Coverage(IEnumerable<int> secondsAfterStart) =>
        new(secondsAfterStart.Select(s => Start.AddSeconds(s)), MaxGap);
}
