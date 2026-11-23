using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Transit.Data;

public class VehiclePositionWriter(TransitDbContext context)
{
    private const string InsertSql = """
        INSERT INTO vehicle_positions
            (vehicle_id, trip_id, route_id, start_date, latitude, longitude, observed_at, stop_id,
             current_stop_sequence, current_status)
        SELECT *
        FROM unnest(
            @vehicle_ids, @trip_ids, @route_ids, @start_dates, @latitudes, @longitudes, @observed_ats, @stop_ids,
            @current_stop_sequences, @current_statuses)
        ON CONFLICT (vehicle_id, observed_at) DO NOTHING
        """;

    public async Task<int> InsertNewAsync(IReadOnlyCollection<VehiclePosition> positions, CancellationToken cancellationToken)
    {
        if (positions.Count == 0)
        {
            return 0;
        }

        return await context.Database.ExecuteSqlRawAsync(
            InsertSql,
            [
                ArrayParameter("vehicle_ids", NpgsqlDbType.Text, positions.Select(p => p.VehicleId)),
                ArrayParameter("trip_ids", NpgsqlDbType.Text, positions.Select(p => p.TripId)),
                ArrayParameter("route_ids", NpgsqlDbType.Text, positions.Select(p => p.RouteId)),
                ArrayParameter("start_dates", NpgsqlDbType.Date, positions.Select(p => p.StartDate)),
                ArrayParameter("latitudes", NpgsqlDbType.Double, positions.Select(p => p.Latitude)),
                ArrayParameter("longitudes", NpgsqlDbType.Double, positions.Select(p => p.Longitude)),
                ArrayParameter("observed_ats", NpgsqlDbType.TimestampTz, positions.Select(p => p.ObservedAt.ToUniversalTime())),
                ArrayParameter("stop_ids", NpgsqlDbType.Text, positions.Select(p => p.StopId)),
                ArrayParameter("current_stop_sequences", NpgsqlDbType.Integer, positions.Select(p => p.CurrentStopSequence)),
                ArrayParameter("current_statuses", NpgsqlDbType.Integer, positions.Select(p => (int?)p.CurrentStatus)),
            ],
            cancellationToken);
    }

    private static NpgsqlParameter ArrayParameter<T>(string name, NpgsqlDbType elementType, IEnumerable<T> values) =>
        new(name, NpgsqlDbType.Array | elementType) { Value = values.ToArray() };
}
