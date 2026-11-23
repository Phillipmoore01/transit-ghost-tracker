using Transit.Gtfs;

namespace Transit.Ingestion;

public static class CompletedServiceDates
{
    public static IReadOnlyList<DateOnly> Within(
        DateTimeOffset now,
        TimeZoneInfo agencyTimeZone,
        TimeSpan serviceDayLength,
        int lookbackDays)
    {
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, agencyTimeZone).DateTime);
        var serviceDayEnd = new GtfsTime(serviceDayLength);

        return Enumerable.Range(0, lookbackDays + 1)
            .Select(offset => localToday.AddDays(offset - lookbackDays))
            .Where(date => serviceDayEnd.ResolveOn(date, agencyTimeZone) <= now)
            .ToArray();
    }
}
