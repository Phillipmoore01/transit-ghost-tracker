using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Transit.Gtfs;
using Transit.Gtfs.Arrivals;

namespace Transit.Data;

public sealed record StopArrivalRunResult(
    DateOnly ServiceDate,
    int TripsObserved,
    int TripsMatched,
    int ArrivalsRecorded);

public class StopArrivalRecorder(TransitDbContext context, TimeProvider timeProvider)
{
    private const int TripBatchSize = 500;

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);

    private const string ObservedTripsSql = """
        SELECT trip_id, min(observed_at)
        FROM vehicle_positions
        WHERE start_date = @service_date
          AND current_stop_sequence IS NOT NULL
          AND current_status IS NOT NULL
        GROUP BY trip_id
        ORDER BY trip_id
        """;

    private const string ScheduledTripsSql = """
        SELECT DISTINCT ON (o.trip_id) o.trip_id, t.feed_id, t.route_id, a.time_zone
        FROM unnest(@trip_ids, @first_seen) AS o(trip_id, first_seen)
        JOIN trips t ON t.trip_id = o.trip_id
        JOIN gtfs_feeds f ON f.id = t.feed_id
        JOIN LATERAL (
            SELECT time_zone FROM agencies WHERE feed_id = t.feed_id ORDER BY id LIMIT 1
        ) a ON true
        WHERE (f.start_date IS NULL OR f.start_date <= @service_date)
          AND (f.end_date IS NULL OR f.end_date >= @service_date)
        ORDER BY o.trip_id, (f.imported_at <= o.first_seen) DESC, f.imported_at DESC, f.id DESC
        """;

    private const string StopTimesSql = """
        SELECT st.trip_id, st.stop_sequence, st.stop_id, coalesce(st.arrival_time, st.departure_time)
        FROM unnest(@feed_ids, @trip_ids) AS k(feed_id, trip_id)
        JOIN stop_times st ON st.feed_id = k.feed_id AND st.trip_id = k.trip_id
        """;

    private const string ReportsSql = """
        SELECT trip_id, vehicle_id, observed_at, current_stop_sequence, current_status
        FROM vehicle_positions
        WHERE start_date = @service_date
          AND trip_id = ANY(@trip_ids)
          AND current_stop_sequence IS NOT NULL
          AND current_status IS NOT NULL
        """;

    private const string InsertArrivalsSql = """
        INSERT INTO observed_stop_arrivals
            (trip_id, service_date, stop_sequence, feed_id, route_id, stop_id, vehicle_id, scheduled_at, observed_at, evidence)
        SELECT trip_id, @service_date, stop_sequence, feed_id, route_id, stop_id, vehicle_id, scheduled_at, observed_at, evidence
        FROM unnest(
            @trip_ids, @stop_sequences, @feed_ids, @route_ids, @stop_ids, @vehicle_ids, @scheduled_ats, @observed_ats, @evidences)
            AS a(trip_id, stop_sequence, feed_id, route_id, stop_id, vehicle_id, scheduled_at, observed_at, evidence)
        """;

    private const string UpsertRunSql = """
        INSERT INTO stop_arrival_runs (service_date, completed_at, trips_observed, trips_matched, arrivals_recorded)
        VALUES (@service_date, @completed_at, @trips_observed, @trips_matched, @arrivals_recorded)
        ON CONFLICT (service_date) DO UPDATE SET
            completed_at = excluded.completed_at,
            trips_observed = excluded.trips_observed,
            trips_matched = excluded.trips_matched,
            arrivals_recorded = excluded.arrivals_recorded
        """;

    public Task<string?> GetLatestAgencyTimeZoneAsync(CancellationToken cancellationToken) =>
        context.Agencies
            .OrderByDescending(a => a.FeedId)
            .ThenBy(a => a.Id)
            .Select(a => (string?)a.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlySet<DateOnly>> GetRecordedServiceDatesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var dates = await context.StopArrivalRuns
            .Where(r => r.ServiceDate >= from && r.ServiceDate <= to)
            .Select(r => r.ServiceDate)
            .ToListAsync(cancellationToken);

        return dates.ToHashSet();
    }

    public async Task<StopArrivalRunResult> RecordAsync(
        DateOnly serviceDate,
        TimeSpan maxInterpolationGap,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        await using (var delete = CreateCommand("DELETE FROM observed_stop_arrivals WHERE service_date = @service_date", connection))
        {
            delete.Parameters.Add(DateParameter(serviceDate));
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        var observedTrips = await ReadObservedTripsAsync(connection, serviceDate, cancellationToken);
        var timeZones = new Dictionary<string, TimeZoneInfo?>();
        var tripsMatched = 0;
        var arrivalsRecorded = 0;

        foreach (var batch in observedTrips.Chunk(TripBatchSize))
        {
            var scheduledTrips = await ReadScheduledTripsAsync(connection, serviceDate, batch, cancellationToken);
            if (scheduledTrips.Count == 0)
            {
                continue;
            }

            var stopsByTrip = await ReadStopTimesAsync(connection, scheduledTrips, cancellationToken);
            var reportsByTrip = await ReadReportsAsync(connection, serviceDate, scheduledTrips.Keys, cancellationToken);
            var rows = new List<ObservedStopArrival>();

            foreach (var (tripId, trip) in scheduledTrips)
            {
                if (ResolveTimeZone(timeZones, trip.TimeZone) is not { } timeZone
                    || !stopsByTrip.TryGetValue(tripId, out var stops))
                {
                    continue;
                }

                tripsMatched++;

                if (!reportsByTrip.TryGetValue(tripId, out var reports))
                {
                    continue;
                }

                var scheduledBySequence = stops.ToDictionary(s => s.StopSequence, s => s.ScheduledArrival);

                foreach (var arrival in StopArrivalInference.Infer(stops, reports, maxInterpolationGap))
                {
                    rows.Add(new ObservedStopArrival
                    {
                        TripId = tripId,
                        ServiceDate = serviceDate,
                        StopSequence = arrival.StopSequence,
                        FeedId = trip.FeedId,
                        RouteId = trip.RouteId,
                        StopId = arrival.StopId,
                        VehicleId = arrival.VehicleId,
                        ScheduledAt = scheduledBySequence[arrival.StopSequence]?.ResolveOn(serviceDate, timeZone),
                        ObservedAt = arrival.ArrivedAt,
                        Evidence = arrival.Evidence,
                    });
                }
            }

            arrivalsRecorded += await InsertArrivalsAsync(connection, serviceDate, rows, cancellationToken);
        }

        var result = new StopArrivalRunResult(serviceDate, observedTrips.Count, tripsMatched, arrivalsRecorded);

        await using (var upsert = CreateCommand(UpsertRunSql, connection))
        {
            upsert.Parameters.Add(DateParameter(serviceDate));
            upsert.Parameters.Add(new NpgsqlParameter("completed_at", NpgsqlDbType.TimestampTz) { Value = timeProvider.GetUtcNow() });
            upsert.Parameters.Add(new NpgsqlParameter("trips_observed", NpgsqlDbType.Integer) { Value = result.TripsObserved });
            upsert.Parameters.Add(new NpgsqlParameter("trips_matched", NpgsqlDbType.Integer) { Value = result.TripsMatched });
            upsert.Parameters.Add(new NpgsqlParameter("arrivals_recorded", NpgsqlDbType.Integer) { Value = result.ArrivalsRecorded });
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<List<(string TripId, DateTimeOffset FirstSeen)>> ReadObservedTripsAsync(
        NpgsqlConnection connection,
        DateOnly serviceDate,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(ObservedTripsSql, connection);
        command.Parameters.Add(DateParameter(serviceDate));

        var trips = new List<(string, DateTimeOffset)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            trips.Add((reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1)));
        }

        return trips;
    }

    private static async Task<Dictionary<string, ScheduledTrip>> ReadScheduledTripsAsync(
        NpgsqlConnection connection,
        DateOnly serviceDate,
        (string TripId, DateTimeOffset FirstSeen)[] observedTrips,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(ScheduledTripsSql, connection);
        command.Parameters.Add(DateParameter(serviceDate));
        command.Parameters.Add(ArrayParameter("trip_ids", NpgsqlDbType.Text, observedTrips.Select(t => t.TripId)));
        command.Parameters.Add(ArrayParameter("first_seen", NpgsqlDbType.TimestampTz, observedTrips.Select(t => t.FirstSeen)));

        var trips = new Dictionary<string, ScheduledTrip>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            trips[reader.GetString(0)] = new ScheduledTrip(reader.GetInt32(1), reader.GetString(2), reader.GetString(3));
        }

        return trips;
    }

    private static async Task<Dictionary<string, List<ScheduledStop>>> ReadStopTimesAsync(
        NpgsqlConnection connection,
        Dictionary<string, ScheduledTrip> scheduledTrips,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(StopTimesSql, connection);
        command.Parameters.Add(ArrayParameter("feed_ids", NpgsqlDbType.Integer, scheduledTrips.Values.Select(t => t.FeedId)));
        command.Parameters.Add(ArrayParameter("trip_ids", NpgsqlDbType.Text, scheduledTrips.Keys));

        var stops = new Dictionary<string, List<ScheduledStop>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var tripId = reader.GetString(0);
            if (!stops.TryGetValue(tripId, out var tripStops))
            {
                stops[tripId] = tripStops = [];
            }

            tripStops.Add(new ScheduledStop(
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : new GtfsTime(reader.GetFieldValue<TimeSpan>(3))));
        }

        return stops;
    }

    private static async Task<Dictionary<string, List<TripProgressReport>>> ReadReportsAsync(
        NpgsqlConnection connection,
        DateOnly serviceDate,
        IEnumerable<string> tripIds,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(ReportsSql, connection);
        command.Parameters.Add(DateParameter(serviceDate));
        command.Parameters.Add(ArrayParameter("trip_ids", NpgsqlDbType.Text, tripIds));

        var reports = new Dictionary<string, List<TripProgressReport>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var tripId = reader.GetString(0);
            if (!reports.TryGetValue(tripId, out var tripReports))
            {
                reports[tripId] = tripReports = [];
            }

            tripReports.Add(new TripProgressReport(
                reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetInt32(3),
                (VehicleStopStatus)reader.GetInt32(4)));
        }

        return reports;
    }

    private static async Task<int> InsertArrivalsAsync(
        NpgsqlConnection connection,
        DateOnly serviceDate,
        List<ObservedStopArrival> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        await using var command = CreateCommand(InsertArrivalsSql, connection);
        command.Parameters.Add(DateParameter(serviceDate));
        command.Parameters.Add(ArrayParameter("trip_ids", NpgsqlDbType.Text, rows.Select(r => r.TripId)));
        command.Parameters.Add(ArrayParameter("stop_sequences", NpgsqlDbType.Integer, rows.Select(r => r.StopSequence)));
        command.Parameters.Add(ArrayParameter("feed_ids", NpgsqlDbType.Integer, rows.Select(r => r.FeedId)));
        command.Parameters.Add(ArrayParameter("route_ids", NpgsqlDbType.Text, rows.Select(r => r.RouteId)));
        command.Parameters.Add(ArrayParameter("stop_ids", NpgsqlDbType.Text, rows.Select(r => r.StopId)));
        command.Parameters.Add(ArrayParameter("vehicle_ids", NpgsqlDbType.Text, rows.Select(r => r.VehicleId)));
        command.Parameters.Add(ArrayParameter("scheduled_ats", NpgsqlDbType.TimestampTz, rows.Select(r => r.ScheduledAt?.ToUniversalTime())));
        command.Parameters.Add(ArrayParameter("observed_ats", NpgsqlDbType.TimestampTz, rows.Select(r => r.ObservedAt.ToUniversalTime())));
        command.Parameters.Add(ArrayParameter("evidences", NpgsqlDbType.Integer, rows.Select(r => (int)r.Evidence)));

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static TimeZoneInfo? ResolveTimeZone(Dictionary<string, TimeZoneInfo?> cache, string timeZoneId)
    {
        if (!cache.TryGetValue(timeZoneId, out var timeZone))
        {
            cache[timeZoneId] = timeZone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var found) ? found : null;
        }

        return timeZone;
    }

    private static NpgsqlCommand CreateCommand(string sql, NpgsqlConnection connection) =>
        new(sql, connection) { CommandTimeout = (int)CommandTimeout.TotalSeconds };

    private static NpgsqlParameter DateParameter(DateOnly serviceDate) =>
        new("service_date", NpgsqlDbType.Date) { Value = serviceDate };

    private static NpgsqlParameter ArrayParameter<T>(string name, NpgsqlDbType elementType, IEnumerable<T> values) =>
        new(name, NpgsqlDbType.Array | elementType) { Value = values.ToArray() };

    private sealed record ScheduledTrip(int FeedId, string RouteId, string TimeZone);
}
