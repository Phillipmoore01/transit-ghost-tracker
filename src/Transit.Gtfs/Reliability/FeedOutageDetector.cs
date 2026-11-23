namespace Transit.Gtfs.Reliability;

public sealed record FeedOutagePeriod(DateTimeOffset LastFeedBefore, DateTimeOffset? FirstFeedAfter);

public static class FeedOutageDetector
{
    public static IReadOnlyList<FeedOutagePeriod> Find(
        IEnumerable<DateTimeOffset> feedTimestamps,
        DateTimeOffset now,
        TimeSpan maxGap)
    {
        var ordered = feedTimestamps.Distinct().Order().ToArray();
        var outages = new List<FeedOutagePeriod>();

        for (var i = 1; i < ordered.Length; i++)
        {
            if (ordered[i] - ordered[i - 1] > maxGap)
            {
                outages.Add(new FeedOutagePeriod(ordered[i - 1], ordered[i]));
            }
        }

        if (ordered.Length > 0 && now - ordered[^1] > maxGap)
        {
            outages.Add(new FeedOutagePeriod(ordered[^1], null));
        }

        return outages;
    }
}
