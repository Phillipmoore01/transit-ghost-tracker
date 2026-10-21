using System.Globalization;

namespace Transit.Gtfs;

public readonly record struct GtfsTime(TimeSpan SinceServiceDayStart)
{
    public static GtfsTime Parse(string value)
    {
        var parts = value.Trim().Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || minutes > 59
            || seconds > 59)
        {
            throw new FormatException($"'{value}' is not a valid GTFS time (H:MM:SS, hours may exceed 23).");
        }

        return new GtfsTime(new TimeSpan(hours, minutes, seconds));
    }

    public DateTimeOffset ResolveOn(DateOnly serviceDate, TimeZoneInfo agencyTimeZone)
    {
        var localNoon = serviceDate.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified);
        var noonUtc = TimeZoneInfo.ConvertTimeToUtc(localNoon, agencyTimeZone);
        var serviceDayStart = new DateTimeOffset(noonUtc, TimeSpan.Zero).AddHours(-12);
        return serviceDayStart + SinceServiceDayStart;
    }
}
