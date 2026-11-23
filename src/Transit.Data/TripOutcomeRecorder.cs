using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Transit.Gtfs;
using Transit.Gtfs.Reliability;

namespace Transit.Data;

public sealed record TripOutcomeRunResult(
    DateOnly ServiceDate,
    int FeedId,
    int TripsScheduled,
    int TripsObserved,
    int TripsNotObserved,
    int TripsInFeedGaps,
    int TripsOnUntrackedRoutes);

public class TripOutcomeRecorder(TransitDbContext context, TimeProvider timeProvider)
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);

    private const string ScheduleFeedSql = """
        SELECT f.id, a.time_zone
        FROM gtfs_feeds f
        JOIN LATERAL (
            SELECT time_zone FROM agencies WHERE feed_id = f.id ORDER BY id LIMIT 1
        ) a ON true
        WHERE (f.start_date IS NULL OR f.start_date <= @service_date)
          AND (f.end_date IS NULL OR f.end_date >= @service_date)
        ORDER BY
            (f.imported_at <= (@service_date::timestamp AT TIME ZONE a.time_zone)) DESC,
            CASE WHEN f.imported_at <= (@service_date::timestamp AT TIME ZONE a.time_zone) THEN f.imported_at END DESC,
            f.imported_at,
            f.id
        LIMIT 1
        """;

    private const string ScheduledTripsSql = """
        WITH active_services AS (
            SELECT service_id
            FROM service_calendars
            WHERE feed_id = @feed_id
              AND @service_date BETWEEN start_date AND end_date
              AND CASE extract(isodow FROM @service_date)
                      WHEN 1 THEN monday
                      WHEN 2 THEN tuesday
                      WHEN 3 THEN wednesday
                      WHEN 4 THEN thursday
                      WHEN 5 THEN friday
                      WHEN 6 THEN saturday
                      ELSE sunday
                  END
            UNION
            SELECT service_id
            FROM calendar_dates
            WHERE feed_id = @feed_id AND date = @service_date AND exception_type = 1
            EXCEPT
            SELECT service_id
            FROM calendar_dates
            WHERE feed_id = @feed_id AND date = @service_date AND exception_type = 2
        )
        SELECT t.trip_id,
               t.route_id,
               t.direction_id,
               min(coalesce(st.departure_time, st.arrival_time)),
               max(coalesce(st.arrival_time, st.departure_time))
        FROM trips t
        JOIN active_services s ON s.service_id = t.service_id
        JOIN stop_times st ON st.feed_id = t.feed_id AND st.trip_id = t.trip_id
        WHERE t.feed_id = @feed_id
        GROUP BY t.trip_id, t.route_id, t.direction_id
        HAVING min(coalesce(st.departure_time, st.arrival_time)) IS NOT NULL
        """;

    private const string ObservedTripsSql = """
        SELECT trip_id, min(observed_at)
        FROM vehicle_positions
        WHERE start_date = @service_date
        GROUP BY trip_id
        """;

    private const string FeedTimestampsSql = """
        SELECT feed_timestamp
        FROM feed_polls
        WHERE feed_timestamp BETWEEN @from AND @to
        """;

    private const string InsertOutcomesSql = """
        INSERT INTO scheduled_trip_outcomes
            (service_date, trip_id, feed_id, route_id, direction_id, scheduled_start, scheduled_end, outcome, first_observed_at)
        SELECT @service_date, trip_id, @feed_id, route_id, direction_id, scheduled_start, scheduled_end, outcome, first_observed_at
        FROM unnest(
            @trip_ids, @route_ids, @direction_ids, @scheduled_starts, @scheduled_ends, @outcomes, @first_observed_ats)
            AS o(trip_id, route_id, direction_id, scheduled_start, scheduled_end, outcome, first_observed_at)
        """;

    private const string UpsertRunSql = """
        INSERT INTO trip_outcome_runs
            (service_date, feed_id, completed_at, trips_scheduled, trips_observed, trips_not_observed,
             trips_in_feed_gaps, trips_on_untracked_routes)
        VALUES
            (@service_date, @feed_id, @completed_at, @trips_scheduled, @trips_observed, @trips_not_observed,
             @trips_in_feed_gaps, @trips_on_untracked_routes)
        ON CONFLICT (service_date) DO UPDATE SET
            feed_id = excluded.feed_id,
            completed_at = excluded.completed_at,
            trips_scheduled = excluded.trips_scheduled,
            trips_observed = excluded.trips_observed,
            trips_not_observed = excluded.trips_not_observed,
            trips_in_feed_gaps = excluded.trips_in_feed_gaps,
            trips_on_untracked_routes = excluded.trips_on_untracked_routes
        """;

    public async Task<IReadOnlySet<DateOnly>> GetRecordedServiceDatesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var dates = await context.TripOutcomeRuns
            .Where(r => r.ServiceDate >= from && r.ServiceDate <= to)
            .Select(r => r.ServiceDate)
            .ToListAsync(cancellationToken);

        return dates.ToHashSet();
    }

    public async Task<TripOutcomeRunResult?> RecordAsync(
        DateOnly serviceDate,
        TimeSpan maxFeedGap,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        if (await ReadScheduleFeedAsync(connection, serviceDate, cancellationToken) is not { } feed
            || !TimeZoneInfo.TryFindSystemTimeZoneById(feed.TimeZoneId, out var timeZone))
        {
            return null;
        }

        var feedId = feed.FeedId;

        var scheduled = await ReadScheduledTripsAsync(connection, serviceDate, feedId, timeZone, cancellationToken);
        var observed = await ReadObservedTripsAsync(connection, serviceDate, cancellationToken);
        var coverage = await ReadCoverageAsync(connection, scheduled, maxFeedGap, cancellationToken);

        var trackedRoutes = scheduled
            .Where(t => observed.ContainsKey(t.TripId))
            .Select(t => t.RouteId)
            .ToHashSet();

        var outcomes = scheduled
            .Select(trip =>
            {
                var firstObserved = observed.TryGetValue(trip.TripId, out var seen) ? seen : (DateTimeOffset?)null;
                var outcome = TripOutcomeClassifier.Classify(
                    firstObserved is not null,
                    trackedRoutes.Contains(trip.RouteId),
                    coverage.Covers(trip.Start, trip.End));
                return (Trip: trip, Outcome: outcome, FirstObserved: firstObserved);
            })
            .ToList();

        await using (var delete = CreateCommand("DELETE FROM scheduled_trip_outcomes WHERE service_date = @service_date", connection))
        {
            delete.Parameters.Add(DateParameter(serviceDate));
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        if (outcomes.Count > 0)
        {
            await using var insert = CreateCommand(InsertOutcomesSql, connection);
            insert.Parameters.Add(DateParameter(serviceDate));
            insert.Parameters.Add(new NpgsqlParameter("feed_id", NpgsqlDbType.Integer) { Value = feedId });
            insert.Parameters.Add(ArrayParameter("trip_ids", NpgsqlDbType.Text, outcomes.Select(o => o.Trip.TripId)));
            insert.Parameters.Add(ArrayParameter("route_ids", NpgsqlDbType.Text, outcomes.Select(o => o.Trip.RouteId)));
            insert.Parameters.Add(ArrayParameter("direction_ids", NpgsqlDbType.Integer, outcomes.Select(o => o.Trip.DirectionId)));
            insert.Parameters.Add(ArrayParameter("scheduled_starts", NpgsqlDbType.TimestampTz, outcomes.Select(o => o.Trip.Start.ToUniversalTime())));
            insert.Parameters.Add(ArrayParameter("scheduled_ends", NpgsqlDbType.TimestampTz, outcomes.Select(o => o.Trip.End.ToUniversalTime())));
            insert.Parameters.Add(ArrayParameter("outcomes", NpgsqlDbType.Integer, outcomes.Select(o => (int)o.Outcome)));
            insert.Parameters.Add(ArrayParameter("first_observed_ats", NpgsqlDbType.TimestampTz, outcomes.Select(o => o.FirstObserved?.ToUniversalTime())));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        var result = new TripOutcomeRunResult(
            serviceDate,
            feedId,
            outcomes.Count,
            outcomes.Count(o => o.Outcome == TripOutcome.Observed),
            outcomes.Count(o => o.Outcome == TripOutcome.NotObserved),
            outcomes.Count(o => o.Outcome == TripOutcome.FeedGap),
            outcomes.Count(o => o.Outcome == TripOutcome.RouteNotTracked));

        await using (var upsert = CreateCommand(UpsertRunSql, connection))
        {
            upsert.Parameters.Add(DateParameter(serviceDate));
            upsert.Parameters.Add(new NpgsqlParameter("feed_id", NpgsqlDbType.Integer) { Value = feedId });
            upsert.Parameters.Add(new NpgsqlParameter("completed_at", NpgsqlDbType.TimestampTz) { Value = timeProvider.GetUtcNow() });
            upsert.Parameters.Add(IntegerParameter("trips_scheduled", result.TripsScheduled));
            upsert.Parameters.Add(IntegerParameter("trips_observed", result.TripsObserved));
            upsert.Parameters.Add(IntegerParameter("trips_not_observed", result.TripsNotObserved));
            upsert.Parameters.Add(IntegerParameter("trips_in_feed_gaps", result.TripsInFeedGaps));
            upsert.Parameters.Add(IntegerParameter("trips_on_untracked_routes", result.TripsOnUntrackedRoutes));
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<(int FeedId, string TimeZoneId)?> ReadScheduleFeedAsync(
        NpgsqlConnection connection,
        DateOnly serviceDate,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(ScheduleFeedSql, connection);
        command.Parameters.Add(DateParameter(serviceDate));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetInt32(0), reader.GetString(1))
            : null;
    }

    private static async Task<List<ScheduledTrip>> ReadScheduledTripsAsync(
        NpgsqlConnection connection,
        DateOnly serviceDate,
        int feedId,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(ScheduledTripsSql, connection);
        command.Parameters.Add(DateParameter(serviceDate));
        command.Parameters.Add(new NpgsqlParameter("feed_id", NpgsqlDbType.Integer) { Value = feedId });

        var trips = new List<ScheduledTrip>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            trips.Add(new ScheduledTrip(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                new GtfsTime(reader.GetFieldValue<TimeSpan>(3)).ResolveOn(serviceDate, timeZone),
                new GtfsTime(reader.GetFieldValue<TimeSpan>(4)).ResolveOn(serviceDate, timeZone)));
        }

        return trips;
    }

    private static async Task<Dictionary<string, DateTimeOffset>> ReadObservedTripsAsync(
        NpgsqlConnection connection,
        DateOnly serviceDate,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(ObservedTripsSql, connection);
        command.Parameters.Add(DateParameter(serviceDate));

        var observed = new Dictionary<string, DateTimeOffset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            observed[reader.GetString(0)] = reader.GetFieldValue<DateTimeOffset>(1);
        }

        return observed;
    }

    private static async Task<FeedCoverage> ReadCoverageAsync(
        NpgsqlConnection connection,
        List<ScheduledTrip> trips,
        TimeSpan maxFeedGap,
        CancellationToken cancellationToken)
    {
        if (trips.Count == 0)
        {
            return new FeedCoverage([], maxFeedGap);
        }

        await using var command = CreateCommand(FeedTimestampsSql, connection);
        command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = trips.Min(t => t.Start).ToUniversalTime() - maxFeedGap });
        command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = trips.Max(t => t.End).ToUniversalTime() + maxFeedGap });

        var timestamps = new List<DateTimeOffset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            timestamps.Add(reader.GetFieldValue<DateTimeOffset>(0));
        }

        return new FeedCoverage(timestamps, maxFeedGap);
    }

    private static NpgsqlCommand CreateCommand(string sql, NpgsqlConnection connection) =>
        new(sql, connection) { CommandTimeout = (int)CommandTimeout.TotalSeconds };

    private static NpgsqlParameter DateParameter(DateOnly serviceDate) =>
        new("service_date", NpgsqlDbType.Date) { Value = serviceDate };

    private static NpgsqlParameter IntegerParameter(string name, int value) =>
        new(name, NpgsqlDbType.Integer) { Value = value };

    private static NpgsqlParameter ArrayParameter<T>(string name, NpgsqlDbType elementType, IEnumerable<T> values) =>
        new(name, NpgsqlDbType.Array | elementType) { Value = values.ToArray() };

    private sealed record ScheduledTrip(
        string TripId,
        string RouteId,
        int? DirectionId,
        DateTimeOffset Start,
        DateTimeOffset End);
}
