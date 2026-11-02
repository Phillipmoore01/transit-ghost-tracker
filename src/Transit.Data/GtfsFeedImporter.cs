using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Transit.Gtfs.Static;

namespace Transit.Data;

public sealed record GtfsImportResult(
    int FeedId,
    string? FeedVersion,
    ulong Agencies,
    ulong Routes,
    ulong Stops,
    ulong Trips,
    ulong StopTimes,
    ulong ServiceCalendars,
    ulong CalendarDates);

public class GtfsFeedImporter(TransitDbContext context, TimeProvider timeProvider)
{
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMinutes(10);

    public Task<bool> IsImportedAsync(string sha256, CancellationToken cancellationToken) =>
        context.GtfsFeeds.AnyAsync(f => f.Sha256 == sha256, cancellationToken);

    public async Task<GtfsImportResult> ImportAsync(
        GtfsStaticFeed source,
        string sha256,
        Uri sourceUrl,
        CancellationToken cancellationToken)
    {
        var info = source.ReadFeedInfo();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var feed = new GtfsFeed
        {
            Sha256 = sha256,
            SourceUrl = sourceUrl.ToString(),
            FeedVersion = info?.FeedVersion,
            StartDate = info?.StartDate,
            EndDate = info?.EndDate,
            ImportedAt = timeProvider.GetUtcNow(),
        };
        context.GtfsFeeds.Add(feed);
        await context.SaveChangesAsync(cancellationToken);

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var feedId = feed.Id;

        var agencies = await CopyAsync(
            connection,
            "agencies (feed_id, agency_id, name, time_zone)",
            source.ReadAgencies(),
            async (row, agency, ct) =>
            {
                await row.WriteAsync(feedId, NpgsqlDbType.Integer, ct);
                await WriteTextAsync(row, agency.AgencyId, ct);
                await WriteTextAsync(row, agency.Name, ct);
                await WriteTextAsync(row, agency.TimeZone, ct);
            },
            cancellationToken);

        var routes = await CopyAsync(
            connection,
            "routes (feed_id, route_id, agency_id, short_name, long_name, route_type)",
            source.ReadRoutes(),
            async (row, route, ct) =>
            {
                await row.WriteAsync(feedId, NpgsqlDbType.Integer, ct);
                await WriteTextAsync(row, route.RouteId, ct);
                await WriteTextAsync(row, route.AgencyId, ct);
                await WriteTextAsync(row, route.ShortName, ct);
                await WriteTextAsync(row, route.LongName, ct);
                await row.WriteAsync(route.RouteType, NpgsqlDbType.Integer, ct);
            },
            cancellationToken);

        var stops = await CopyAsync(
            connection,
            "stops (feed_id, stop_id, name, latitude, longitude, location_type, parent_station)",
            source.ReadStops(),
            async (row, stop, ct) =>
            {
                await row.WriteAsync(feedId, NpgsqlDbType.Integer, ct);
                await WriteTextAsync(row, stop.StopId, ct);
                await WriteTextAsync(row, stop.Name, ct);
                await WriteValueAsync(row, stop.Latitude, NpgsqlDbType.Double, ct);
                await WriteValueAsync(row, stop.Longitude, NpgsqlDbType.Double, ct);
                await row.WriteAsync(stop.LocationType, NpgsqlDbType.Integer, ct);
                await WriteTextAsync(row, stop.ParentStation, ct);
            },
            cancellationToken);

        var trips = await CopyAsync(
            connection,
            "trips (feed_id, trip_id, route_id, service_id, direction_id, headsign, block_id, shape_id)",
            source.ReadTrips(),
            async (row, trip, ct) =>
            {
                await row.WriteAsync(feedId, NpgsqlDbType.Integer, ct);
                await WriteTextAsync(row, trip.TripId, ct);
                await WriteTextAsync(row, trip.RouteId, ct);
                await WriteTextAsync(row, trip.ServiceId, ct);
                await WriteValueAsync(row, trip.DirectionId, NpgsqlDbType.Integer, ct);
                await WriteTextAsync(row, trip.Headsign, ct);
                await WriteTextAsync(row, trip.BlockId, ct);
                await WriteTextAsync(row, trip.ShapeId, ct);
            },
            cancellationToken);

        var stopTimes = await CopyAsync(
            connection,
            "stop_times (feed_id, trip_id, stop_sequence, stop_id, arrival_time, departure_time)",
            source.ReadStopTimes(),
            async (row, stopTime, ct) =>
            {
                await row.WriteAsync(feedId, NpgsqlDbType.Integer, ct);
                await WriteTextAsync(row, stopTime.TripId, ct);
                await row.WriteAsync(stopTime.StopSequence, NpgsqlDbType.Integer, ct);
                await WriteTextAsync(row, stopTime.StopId, ct);
                await WriteValueAsync(row, stopTime.ArrivalTime?.SinceServiceDayStart, NpgsqlDbType.Interval, ct);
                await WriteValueAsync(row, stopTime.DepartureTime?.SinceServiceDayStart, NpgsqlDbType.Interval, ct);
            },
            cancellationToken);

        var serviceCalendars = await CopyAsync(
            connection,
            "service_calendars (feed_id, service_id, monday, tuesday, wednesday, thursday, friday, saturday, sunday, start_date, end_date)",
            source.ReadCalendar(),
            async (row, calendar, ct) =>
            {
                await row.WriteAsync(feedId, NpgsqlDbType.Integer, ct);
                await WriteTextAsync(row, calendar.ServiceId, ct);
                await row.WriteAsync(calendar.Monday, NpgsqlDbType.Boolean, ct);
                await row.WriteAsync(calendar.Tuesday, NpgsqlDbType.Boolean, ct);
                await row.WriteAsync(calendar.Wednesday, NpgsqlDbType.Boolean, ct);
                await row.WriteAsync(calendar.Thursday, NpgsqlDbType.Boolean, ct);
                await row.WriteAsync(calendar.Friday, NpgsqlDbType.Boolean, ct);
                await row.WriteAsync(calendar.Saturday, NpgsqlDbType.Boolean, ct);
                await row.WriteAsync(calendar.Sunday, NpgsqlDbType.Boolean, ct);
                await row.WriteAsync(calendar.StartDate, NpgsqlDbType.Date, ct);
                await row.WriteAsync(calendar.EndDate, NpgsqlDbType.Date, ct);
            },
            cancellationToken);

        var calendarDates = await CopyAsync(
            connection,
            "calendar_dates (feed_id, service_id, date, exception_type)",
            source.ReadCalendarDates(),
            async (row, calendarDate, ct) =>
            {
                await row.WriteAsync(feedId, NpgsqlDbType.Integer, ct);
                await WriteTextAsync(row, calendarDate.ServiceId, ct);
                await row.WriteAsync(calendarDate.Date, NpgsqlDbType.Date, ct);
                await row.WriteAsync((int)calendarDate.ExceptionType, NpgsqlDbType.Integer, ct);
            },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new GtfsImportResult(
            feedId,
            feed.FeedVersion,
            agencies,
            routes,
            stops,
            trips,
            stopTimes,
            serviceCalendars,
            calendarDates);
    }

    private static async Task<ulong> CopyAsync<T>(
        NpgsqlConnection connection,
        string target,
        IEnumerable<T> records,
        Func<NpgsqlBinaryImporter, T, CancellationToken, Task> writeRow,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync(
            $"COPY {target} FROM STDIN (FORMAT BINARY)",
            cancellationToken);
        importer.Timeout = CopyTimeout;

        foreach (var record in records)
        {
            await importer.StartRowAsync(cancellationToken);
            await writeRow(importer, record, cancellationToken);
        }

        return await importer.CompleteAsync(cancellationToken);
    }

    private static Task WriteTextAsync(NpgsqlBinaryImporter row, string? value, CancellationToken cancellationToken) =>
        value is null
            ? row.WriteNullAsync(cancellationToken)
            : row.WriteAsync(value, NpgsqlDbType.Text, cancellationToken);

    private static Task WriteValueAsync<T>(
        NpgsqlBinaryImporter row,
        T? value,
        NpgsqlDbType type,
        CancellationToken cancellationToken)
        where T : struct =>
        value is { } present
            ? row.WriteAsync(present, type, cancellationToken)
            : row.WriteNullAsync(cancellationToken);
}
