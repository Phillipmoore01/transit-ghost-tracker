namespace Transit.Data;

public class TripOutcomeRun
{
    public DateOnly ServiceDate { get; set; }

    public int FeedId { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public int TripsScheduled { get; set; }

    public int TripsObserved { get; set; }

    public int TripsNotObserved { get; set; }

    public int TripsInFeedGaps { get; set; }

    public int TripsOnUntrackedRoutes { get; set; }
}
