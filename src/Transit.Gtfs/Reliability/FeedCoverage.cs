namespace Transit.Gtfs.Reliability;

public sealed class FeedCoverage
{
    private readonly DateTimeOffset[] timestamps;
    private readonly int[] gapsBefore;
    private readonly TimeSpan maxGap;

    public FeedCoverage(IEnumerable<DateTimeOffset> feedTimestamps, TimeSpan maxGap)
    {
        timestamps = feedTimestamps.Distinct().Order().ToArray();
        this.maxGap = maxGap;

        gapsBefore = new int[timestamps.Length];
        for (var i = 1; i < timestamps.Length; i++)
        {
            gapsBefore[i] = gapsBefore[i - 1] + (timestamps[i] - timestamps[i - 1] > maxGap ? 1 : 0);
        }
    }

    public bool Covers(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var first = LowerBound(from);
        var last = LowerBound(to.AddTicks(1)) - 1;

        if (first > last)
        {
            return to - from <= maxGap;
        }

        return timestamps[first] - from <= maxGap
            && to - timestamps[last] <= maxGap
            && gapsBefore[last] == gapsBefore[first];
    }

    private int LowerBound(DateTimeOffset value)
    {
        var index = Array.BinarySearch(timestamps, value);
        return index >= 0 ? index : ~index;
    }
}
