namespace Transit.Data.Reports;

public sealed record ReportPeriod(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
}

public enum DayType
{
    Weekday = 0,
    Saturday = 1,
    Sunday = 2,
}

public enum TimeOfDay
{
    AmPeak = 0,
    Midday = 1,
    PmPeak = 2,
    Evening = 3,
    Night = 4,
}

public sealed record RouteName(string RouteId, string? ShortName, string? LongName, int? RouteType)
{
    public string DisplayName =>
        (ShortName, LongName) switch
        {
            ({ Length: > 0 } shortName, { Length: > 0 } longName) => $"{shortName} – {longName}",
            ({ Length: > 0 } shortName, _) => shortName,
            (_, { Length: > 0 } longName) => longName,
            _ => RouteId,
        };
}

public sealed record TripCounts(int Tracked, int NotRun)
{
    public double? NotRunShare => Tracked == 0 ? null : (double)NotRun / Tracked;
}

public sealed record ArrivalStats(int Measured, int Early, int OnTime, int Late, double? MedianDelaySeconds)
{
    public static readonly TimeSpan EarlyBefore = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan LateAfter = TimeSpan.FromMinutes(5);

    public static ArrivalStats None { get; } = new(0, 0, 0, 0, null);

    public double? OnTimeShare => Measured == 0 ? null : (double)OnTime / Measured;
}

public sealed record RouteSummary(RouteName Route, TripCounts Trips, ArrivalStats Arrivals);

public sealed record TripReliabilityCell(DayType DayType, TimeOfDay TimeOfDay, TripCounts Trips);

public sealed record MissingTrip(
    DateOnly ServiceDate,
    string TripId,
    int? DirectionId,
    string? Headsign,
    DateTimeOffset ScheduledStart);

public sealed record RouteReport(
    RouteName Route,
    TripCounts Trips,
    int TripsInFeedGaps,
    IReadOnlyList<TripReliabilityCell> Cells,
    ArrivalStats Arrivals,
    IReadOnlyList<MissingTrip> RecentMissingTrips);

public sealed record StopMatch(string StopId, string Name, bool IsStation);

public sealed record StopRouteArrivals(RouteName Route, ArrivalStats Arrivals);

public sealed record StopReport(string StopId, string Name, IReadOnlyList<StopRouteArrivals> Routes);
