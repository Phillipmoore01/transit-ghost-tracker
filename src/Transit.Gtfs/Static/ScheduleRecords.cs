namespace Transit.Gtfs.Static;

public sealed record FeedInfoRecord(string? FeedVersion, DateOnly? StartDate, DateOnly? EndDate);

public sealed record AgencyRecord(string? AgencyId, string Name, string TimeZone);

public sealed record RouteRecord(string RouteId, string? AgencyId, string? ShortName, string? LongName, int RouteType);

public sealed record StopRecord(
    string StopId,
    string? Name,
    double? Latitude,
    double? Longitude,
    int LocationType,
    string? ParentStation);

public sealed record TripRecord(
    string TripId,
    string RouteId,
    string ServiceId,
    int? DirectionId,
    string? Headsign,
    string? BlockId,
    string? ShapeId);

public sealed record StopTimeRecord(
    string TripId,
    int StopSequence,
    string StopId,
    GtfsTime? ArrivalTime,
    GtfsTime? DepartureTime);

public sealed record CalendarRecord(
    string ServiceId,
    bool Monday,
    bool Tuesday,
    bool Wednesday,
    bool Thursday,
    bool Friday,
    bool Saturday,
    bool Sunday,
    DateOnly StartDate,
    DateOnly EndDate);

public enum CalendarExceptionType
{
    Added = 1,
    Removed = 2,
}

public sealed record CalendarDateRecord(string ServiceId, DateOnly Date, CalendarExceptionType ExceptionType);
