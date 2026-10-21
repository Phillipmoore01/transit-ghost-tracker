namespace Transit.Gtfs;

public sealed record VehicleObservation(
    string VehicleId,
    string TripId,
    string? RouteId,
    DateOnly? StartDate,
    double Latitude,
    double Longitude,
    DateTimeOffset ObservedAt,
    string? StopId);
