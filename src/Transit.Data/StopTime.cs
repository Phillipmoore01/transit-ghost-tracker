namespace Transit.Data;

public class StopTime
{
    public int FeedId { get; set; }

    public required string TripId { get; set; }

    public int StopSequence { get; set; }

    public required string StopId { get; set; }

    public TimeSpan? ArrivalTime { get; set; }

    public TimeSpan? DepartureTime { get; set; }
}
