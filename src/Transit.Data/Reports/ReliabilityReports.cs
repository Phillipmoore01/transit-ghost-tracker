using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Transit.Gtfs.Reliability;

namespace Transit.Data.Reports;

public class ReliabilityReports(TransitDbContext context)
{
    private const int MissingTripLimit = 50;
    private const int StopMatchLimit = 50;

    private static readonly string DelaySeconds = "extract(epoch FROM observed_at - scheduled_at)::double precision";

    private static readonly string ArrivalAggregates = $"""
        count(*),
        count(*) FILTER (WHERE d < -{ArrivalStats.EarlyBefore.TotalSeconds}),
        count(*) FILTER (WHERE d BETWEEN -{ArrivalStats.EarlyBefore.TotalSeconds} AND {ArrivalStats.LateAfter.TotalSeconds}),
        count(*) FILTER (WHERE d > {ArrivalStats.LateAfter.TotalSeconds}),
        percentile_cont(0.5) WITHIN GROUP (ORDER BY d)
        """;

    private static readonly string RouteSummariesSql = $"""
        WITH trip_counts AS (
            SELECT route_id,
                   count(*) FILTER (WHERE outcome IN ({(int)TripOutcome.Observed}, {(int)TripOutcome.NotObserved})) AS tracked,
                   count(*) FILTER (WHERE outcome = {(int)TripOutcome.NotObserved}) AS not_run
            FROM scheduled_trip_outcomes
            WHERE service_date BETWEEN @from AND @to
            GROUP BY route_id
        ),
        arrival_counts AS (
            SELECT route_id, {ArrivalAggregates}
            FROM (
                SELECT route_id, {DelaySeconds} AS d
                FROM observed_stop_arrivals
                WHERE service_date BETWEEN @from AND @to AND scheduled_at IS NOT NULL
            ) a
            GROUP BY route_id
        )
        SELECT t.route_id, r.short_name, r.long_name, r.route_type, t.tracked, t.not_run, a.*
        FROM trip_counts t
        LEFT JOIN arrival_counts a ON a.route_id = t.route_id
        LEFT JOIN routes r ON r.feed_id = (SELECT max(id) FROM gtfs_feeds) AND r.route_id = t.route_id
        WHERE t.tracked > 0
        """;

    private const string RouteNameSql = """
        SELECT route_id, short_name, long_name, route_type
        FROM routes
        WHERE feed_id = (SELECT max(id) FROM gtfs_feeds) AND route_id = @route_id
        """;

    private const string RouteTripsSql = """
        SELECT service_date, scheduled_start, outcome
        FROM scheduled_trip_outcomes
        WHERE route_id = @route_id AND service_date BETWEEN @from AND @to
        """;

    private static readonly string RouteArrivalsSql = $"""
        SELECT {ArrivalAggregates}
        FROM (
            SELECT {DelaySeconds} AS d
            FROM observed_stop_arrivals
            WHERE route_id = @route_id AND service_date BETWEEN @from AND @to AND scheduled_at IS NOT NULL
        ) a
        """;

    private static readonly string MissingTripsSql = $"""
        SELECT o.service_date, o.trip_id, o.direction_id, t.headsign, o.scheduled_start
        FROM scheduled_trip_outcomes o
        LEFT JOIN trips t ON t.feed_id = o.feed_id AND t.trip_id = o.trip_id
        WHERE o.route_id = @route_id
          AND o.service_date BETWEEN @from AND @to
          AND o.outcome = {(int)TripOutcome.NotObserved}
        ORDER BY o.scheduled_start DESC
        LIMIT {MissingTripLimit}
        """;

    private static readonly string StopSearchSql = $"""
        SELECT stop_id, name, location_type
        FROM stops
        WHERE feed_id = (SELECT max(id) FROM gtfs_feeds)
          AND location_type IN (0, 1)
          AND name ILIKE @pattern ESCAPE '\'
          AND (location_type = 1 OR parent_station IS NULL)
        ORDER BY name, stop_id
        LIMIT {StopMatchLimit}
        """;

    private const string StopNameSql = """
        SELECT stop_id, coalesce(name, stop_id)
        FROM stops
        WHERE feed_id = (SELECT max(id) FROM gtfs_feeds) AND stop_id = @stop_id
        """;

    private static readonly string StopArrivalsSql = $"""
        SELECT a.route_id, r.short_name, r.long_name, r.route_type, {ArrivalAggregates}
        FROM (
            SELECT route_id, {DelaySeconds} AS d
            FROM observed_stop_arrivals
            WHERE service_date BETWEEN @from AND @to
              AND scheduled_at IS NOT NULL
              AND stop_id IN (
                  SELECT @stop_id
                  UNION
                  SELECT stop_id FROM stops
                  WHERE feed_id = (SELECT max(id) FROM gtfs_feeds) AND parent_station = @stop_id)
        ) a
        LEFT JOIN routes r ON r.feed_id = (SELECT max(id) FROM gtfs_feeds) AND r.route_id = a.route_id
        GROUP BY a.route_id, r.short_name, r.long_name, r.route_type
        """;

    public async Task<ReportPeriod?> GetLatestPeriodAsync(int days, CancellationToken cancellationToken)
    {
        var latest = await context.TripOutcomeRuns.MaxAsync(r => (DateOnly?)r.ServiceDate, cancellationToken);
        return latest is { } to ? new ReportPeriod(to.AddDays(1 - days), to) : null;
    }

    public async Task<TimeZoneInfo?> GetAgencyTimeZoneAsync(CancellationToken cancellationToken)
    {
        var timeZoneId = await context.Agencies
            .OrderByDescending(a => a.FeedId)
            .ThenBy(a => a.Id)
            .Select(a => a.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);

        return timeZoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone)
            ? timeZone
            : null;
    }

    public async Task<IReadOnlyList<RouteSummary>> GetRouteSummariesAsync(
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(RouteSummariesSql, cancellationToken);
        AddPeriod(command, period);

        var routes = new List<RouteSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            routes.Add(new RouteSummary(
                ReadRouteName(reader, 0),
                new TripCounts((int)reader.GetInt64(4), (int)reader.GetInt64(5)),
                reader.IsDBNull(6) ? ArrivalStats.None : ReadArrivalStats(reader, 7)));
        }

        return routes.OrderBy(r => r.Route, RouteNameOrder.Instance).ToArray();
    }

    public async Task<RouteReport?> GetRouteReportAsync(
        string routeId,
        ReportPeriod period,
        TimeZoneInfo agencyTimeZone,
        CancellationToken cancellationToken)
    {
        var route = await ReadSingleAsync(
            RouteNameSql,
            command => command.Parameters.Add(Text("route_id", routeId)),
            reader => ReadRouteName(reader, 0),
            cancellationToken);

        var cells = new Dictionary<(DayType, TimeOfDay), (int Tracked, int NotRun)>();
        var tripsInFeedGaps = 0;
        var anyTrips = false;

        await using (var command = await CreateCommandAsync(RouteTripsSql, cancellationToken))
        {
            command.Parameters.Add(Text("route_id", routeId));
            AddPeriod(command, period);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                anyTrips = true;
                var outcome = (TripOutcome)reader.GetInt32(2);
                if (outcome == TripOutcome.FeedGap)
                {
                    tripsInFeedGaps++;
                }

                if (outcome is not (TripOutcome.Observed or TripOutcome.NotObserved))
                {
                    continue;
                }

                var key = (
                    ServicePeriods.DayTypeOf(reader.GetFieldValue<DateOnly>(0)),
                    ServicePeriods.TimeOfDayOf(reader.GetFieldValue<DateTimeOffset>(1), agencyTimeZone));
                var (tracked, notRun) = cells.GetValueOrDefault(key);
                cells[key] = (tracked + 1, notRun + (outcome == TripOutcome.NotObserved ? 1 : 0));
            }
        }

        if (route is null && !anyTrips)
        {
            return null;
        }

        var arrivals = await ReadSingleAsync(
            RouteArrivalsSql,
            command =>
            {
                command.Parameters.Add(Text("route_id", routeId));
                AddPeriod(command, period);
            },
            reader => ReadArrivalStats(reader, 0),
            cancellationToken) ?? ArrivalStats.None;

        var missingTrips = new List<MissingTrip>();
        await using (var command = await CreateCommandAsync(MissingTripsSql, cancellationToken))
        {
            command.Parameters.Add(Text("route_id", routeId));
            AddPeriod(command, period);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                missingTrips.Add(new MissingTrip(
                    reader.GetFieldValue<DateOnly>(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4)));
            }
        }

        var cellList = cells
            .Select(c => new TripReliabilityCell(c.Key.Item1, c.Key.Item2, new TripCounts(c.Value.Tracked, c.Value.NotRun)))
            .OrderBy(c => c.DayType)
            .ThenBy(c => c.TimeOfDay)
            .ToArray();

        return new RouteReport(
            route ?? new RouteName(routeId, null, null, null),
            new TripCounts(cellList.Sum(c => c.Trips.Tracked), cellList.Sum(c => c.Trips.NotRun)),
            tripsInFeedGaps,
            cellList,
            arrivals,
            missingTrips);
    }

    public async Task<IReadOnlyList<StopMatch>> SearchStopsAsync(string query, CancellationToken cancellationToken)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return [];
        }

        var pattern = "%" + trimmed.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";

        await using var command = await CreateCommandAsync(StopSearchSql, cancellationToken);
        command.Parameters.Add(Text("pattern", pattern));

        var matches = new List<StopMatch>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            matches.Add(new StopMatch(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) == 1));
        }

        return matches;
    }

    public async Task<StopReport?> GetStopReportAsync(
        string stopId,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        var name = await ReadSingleAsync(
            StopNameSql,
            command => command.Parameters.Add(Text("stop_id", stopId)),
            reader => reader.GetString(1),
            cancellationToken);

        if (name is null)
        {
            return null;
        }

        var routes = new List<StopRouteArrivals>();
        await using (var command = await CreateCommandAsync(StopArrivalsSql, cancellationToken))
        {
            command.Parameters.Add(Text("stop_id", stopId));
            AddPeriod(command, period);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                routes.Add(new StopRouteArrivals(ReadRouteName(reader, 0), ReadArrivalStats(reader, 4)));
            }
        }

        return new StopReport(stopId, name, routes.OrderBy(r => r.Route, RouteNameOrder.Instance).ToArray());
    }

    private async Task<NpgsqlCommand> CreateCommandAsync(string sql, CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        return new NpgsqlCommand(sql, (NpgsqlConnection)context.Database.GetDbConnection());
    }

    private async Task<T?> ReadSingleAsync<T>(
        string sql,
        Action<NpgsqlCommand> configure,
        Func<DbDataReader, T> read,
        CancellationToken cancellationToken)
        where T : class
    {
        await using var command = await CreateCommandAsync(sql, cancellationToken);
        configure(command);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? read(reader) : null;
    }

    private static RouteName ReadRouteName(DbDataReader reader, int ordinal) =>
        new(
            reader.GetString(ordinal),
            reader.IsDBNull(ordinal + 1) ? null : reader.GetString(ordinal + 1),
            reader.IsDBNull(ordinal + 2) ? null : reader.GetString(ordinal + 2),
            reader.IsDBNull(ordinal + 3) ? null : reader.GetInt32(ordinal + 3));

    private static ArrivalStats ReadArrivalStats(DbDataReader reader, int ordinal) =>
        new(
            (int)reader.GetInt64(ordinal),
            (int)reader.GetInt64(ordinal + 1),
            (int)reader.GetInt64(ordinal + 2),
            (int)reader.GetInt64(ordinal + 3),
            reader.IsDBNull(ordinal + 4) ? null : reader.GetDouble(ordinal + 4));

    private static void AddPeriod(NpgsqlCommand command, ReportPeriod period)
    {
        command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = period.From });
        command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = period.To });
    }

    private static NpgsqlParameter Text(string name, string value) =>
        new(name, NpgsqlDbType.Text) { Value = value };

    private sealed class RouteNameOrder : IComparer<RouteName>
    {
        public static readonly RouteNameOrder Instance = new();

        public int Compare(RouteName? x, RouteName? y)
        {
            if (x is null || y is null)
            {
                return Comparer<RouteName?>.Default.Compare(x, y);
            }

            var byType = Nullable.Compare(x.RouteType, y.RouteType);
            if (byType != 0)
            {
                return byType;
            }

            var xLabel = x.ShortName ?? x.LongName ?? x.RouteId;
            var yLabel = y.ShortName ?? y.LongName ?? y.RouteId;
            var xNumber = LeadingNumber(xLabel);
            var yNumber = LeadingNumber(yLabel);

            if (xNumber is not null && yNumber is not null && xNumber != yNumber)
            {
                return xNumber.Value.CompareTo(yNumber.Value);
            }

            if (xNumber is null != yNumber is null)
            {
                return xNumber is null ? 1 : -1;
            }

            var byLabel = string.Compare(xLabel, yLabel, StringComparison.OrdinalIgnoreCase);
            return byLabel != 0 ? byLabel : string.CompareOrdinal(x.RouteId, y.RouteId);
        }

        private static int? LeadingNumber(string label)
        {
            var digits = label.TakeWhile(char.IsAsciiDigit).Count();
            return digits > 0 && int.TryParse(label.AsSpan(0, Math.Min(digits, 9)), out var number) ? number : null;
        }
    }
}
