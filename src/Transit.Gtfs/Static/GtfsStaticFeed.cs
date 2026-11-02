using System.Globalization;
using System.IO.Compression;
using CsvHelper;
using CsvHelper.Configuration;

namespace Transit.Gtfs.Static;

public sealed class GtfsStaticFeed(ZipArchive archive) : IDisposable
{
    private static readonly CsvConfiguration CsvConfiguration = new(CultureInfo.InvariantCulture)
    {
        BadDataFound = null,
    };

    public static GtfsStaticFeed Open(Stream zip) => new(new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: false));

    public FeedInfoRecord? ReadFeedInfo() =>
        Read("feed_info.txt", required: false, row => new FeedInfoRecord(
            row.Optional("feed_version"),
            row.OptionalDate("feed_start_date"),
            row.OptionalDate("feed_end_date")))
        .FirstOrDefault();

    public IEnumerable<AgencyRecord> ReadAgencies() =>
        Read("agency.txt", required: true, row => new AgencyRecord(
            row.Optional("agency_id"),
            row.Required("agency_name"),
            row.Required("agency_timezone")));

    public IEnumerable<RouteRecord> ReadRoutes() =>
        Read("routes.txt", required: true, row => new RouteRecord(
            row.Required("route_id"),
            row.Optional("agency_id"),
            row.Optional("route_short_name"),
            row.Optional("route_long_name"),
            row.RequiredInt("route_type")));

    public IEnumerable<StopRecord> ReadStops() =>
        Read("stops.txt", required: true, row => new StopRecord(
            row.Required("stop_id"),
            row.Optional("stop_name"),
            row.OptionalDouble("stop_lat"),
            row.OptionalDouble("stop_lon"),
            row.OptionalInt("location_type") ?? 0,
            row.Optional("parent_station")));

    public IEnumerable<TripRecord> ReadTrips() =>
        Read("trips.txt", required: true, row => new TripRecord(
            row.Required("trip_id"),
            row.Required("route_id"),
            row.Required("service_id"),
            row.OptionalInt("direction_id"),
            row.Optional("trip_headsign"),
            row.Optional("block_id"),
            row.Optional("shape_id")));

    public IEnumerable<StopTimeRecord> ReadStopTimes() =>
        Read("stop_times.txt", required: true, row => new StopTimeRecord(
            row.Required("trip_id"),
            row.RequiredInt("stop_sequence"),
            row.Required("stop_id"),
            row.OptionalTime("arrival_time"),
            row.OptionalTime("departure_time")));

    public IEnumerable<CalendarRecord> ReadCalendar() =>
        Read("calendar.txt", required: false, row => new CalendarRecord(
            row.Required("service_id"),
            row.RequiredFlag("monday"),
            row.RequiredFlag("tuesday"),
            row.RequiredFlag("wednesday"),
            row.RequiredFlag("thursday"),
            row.RequiredFlag("friday"),
            row.RequiredFlag("saturday"),
            row.RequiredFlag("sunday"),
            row.RequiredDate("start_date"),
            row.RequiredDate("end_date")));

    public IEnumerable<CalendarDateRecord> ReadCalendarDates() =>
        Read("calendar_dates.txt", required: false, row => new CalendarDateRecord(
            row.Required("service_id"),
            row.RequiredDate("date"),
            row.RequiredInt("exception_type") switch
            {
                1 => CalendarExceptionType.Added,
                2 => CalendarExceptionType.Removed,
                var other => throw row.Error($"field 'exception_type' value '{other}' is not 1 or 2"),
            }));

    public void Dispose() => archive.Dispose();

    private IEnumerable<T> Read<T>(string fileName, bool required, Func<GtfsCsvRow, T> map)
    {
        var entry = archive.GetEntry(fileName);
        if (entry is null)
        {
            if (required)
            {
                throw new FormatException($"GTFS feed is missing required file {fileName}.");
            }

            yield break;
        }

        using var reader = new StreamReader(entry.Open(), detectEncodingFromByteOrderMarks: true);
        using var parser = new CsvParser(reader, CsvConfiguration);

        if (!parser.Read())
        {
            yield break;
        }

        var columns = parser.Record!
            .Select((name, index) => (Name: name.Trim(), Index: index))
            .GroupBy(column => column.Name)
            .ToDictionary(group => group.Key, group => group.First().Index);
        var row = new GtfsCsvRow(fileName, parser, columns);

        while (parser.Read())
        {
            if (parser.Record is [var only] && string.IsNullOrWhiteSpace(only))
            {
                continue;
            }

            yield return map(row);
        }
    }
}
