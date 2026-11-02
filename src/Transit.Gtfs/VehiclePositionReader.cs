using System.Globalization;
using TransitRealtime;

namespace Transit.Gtfs;

public static class VehiclePositionReader
{
    public static IEnumerable<VehicleObservation> Read(FeedMessage feed)
    {
        foreach (var entity in feed.Entity)
        {
            if (entity.IsDeleted || entity.Vehicle is not { HasTimestamp: true } vehicle)
            {
                continue;
            }

            var vehicleId = vehicle.Vehicle?.Id;
            var tripId = vehicle.Trip?.TripId;
            var position = vehicle.Position;

            if (string.IsNullOrEmpty(vehicleId) || string.IsNullOrEmpty(tripId) || position is null)
            {
                continue;
            }

            var stopSequence = vehicle.HasCurrentStopSequence ? (int?)vehicle.CurrentStopSequence : null;
            var stopId = NullIfEmpty(vehicle.StopId);

            yield return new VehicleObservation(
                vehicleId,
                tripId,
                NullIfEmpty(vehicle.Trip!.RouteId),
                ParseStartDate(vehicle.Trip.StartDate),
                position.Latitude,
                position.Longitude,
                DateTimeOffset.FromUnixTimeSeconds((long)vehicle.Timestamp),
                stopId,
                stopSequence,
                stopSequence is null && stopId is null ? null : (VehicleStopStatus)vehicle.CurrentStatus);
        }
    }

    private static DateOnly? ParseStartDate(string value) =>
        DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}
