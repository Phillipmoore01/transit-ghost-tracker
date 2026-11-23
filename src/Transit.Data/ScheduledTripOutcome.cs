using Transit.Gtfs.Reliability;

namespace Transit.Data;

public class ScheduledTripOutcome
{
    public DateOnly ServiceDate { get; set; }

    public required string TripId { get; set; }

    public int FeedId { get; set; }

    public required string RouteId { get; set; }

    public int? DirectionId { get; set; }

    public DateTimeOffset ScheduledStart { get; set; }

    public DateTimeOffset ScheduledEnd { get; set; }

    public TripOutcome Outcome { get; set; }

    public DateTimeOffset? FirstObservedAt { get; set; }
}
