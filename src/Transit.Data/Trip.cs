namespace Transit.Data;

public class Trip
{
    public int FeedId { get; set; }

    public required string TripId { get; set; }

    public required string RouteId { get; set; }

    public required string ServiceId { get; set; }

    public int? DirectionId { get; set; }

    public string? Headsign { get; set; }

    public string? BlockId { get; set; }

    public string? ShapeId { get; set; }
}
