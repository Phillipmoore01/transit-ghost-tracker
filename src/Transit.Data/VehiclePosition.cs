namespace Transit.Data;

public class VehiclePosition
{
    public long Id { get; set; }

    public required string VehicleId { get; set; }

    public required string TripId { get; set; }

    public string? RouteId { get; set; }

    public DateOnly? StartDate { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public DateTimeOffset ObservedAt { get; set; }

    public string? StopId { get; set; }
}
