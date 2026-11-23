using Transit.Gtfs.Arrivals;

namespace Transit.Data;

public class ObservedStopArrival
{
    public required string TripId { get; set; }

    public DateOnly ServiceDate { get; set; }

    public int StopSequence { get; set; }

    public int FeedId { get; set; }

    public required string RouteId { get; set; }

    public required string StopId { get; set; }

    public required string VehicleId { get; set; }

    public DateTimeOffset? ScheduledAt { get; set; }

    public DateTimeOffset ObservedAt { get; set; }

    public ArrivalEvidence Evidence { get; set; }
}
