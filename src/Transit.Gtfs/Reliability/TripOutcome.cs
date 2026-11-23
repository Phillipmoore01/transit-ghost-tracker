namespace Transit.Gtfs.Reliability;

public enum TripOutcome
{
    Observed = 0,
    NotObserved = 1,
    FeedGap = 2,
    RouteNotTracked = 3,
}

public static class TripOutcomeClassifier
{
    public static TripOutcome Classify(bool observed, bool routeTracked, bool feedCoveredTrip) =>
        observed ? TripOutcome.Observed
        : !routeTracked ? TripOutcome.RouteNotTracked
        : !feedCoveredTrip ? TripOutcome.FeedGap
        : TripOutcome.NotObserved;
}
