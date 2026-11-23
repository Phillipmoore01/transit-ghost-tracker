namespace Transit.Data;

public class StopArrivalRun
{
    public DateOnly ServiceDate { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public int TripsObserved { get; set; }

    public int TripsMatched { get; set; }

    public int ArrivalsRecorded { get; set; }
}
