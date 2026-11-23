using System.Globalization;
using Transit.Data.Reports;

namespace Transit.Web;

public static class ReportFormat
{
    public const int SmallSample = 10;

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    public static string Percent(double? share) =>
        share is { } value ? value.ToString("0%", Culture) : "–";

    public static string Count(int value) => value.ToString("N0", Culture);

    public static string Share(TripCounts trips) =>
        trips.Tracked == 0 ? "–" : $"{Percent(trips.NotRunShare)} ({Count(trips.NotRun)} of {Count(trips.Tracked)})";

    public static string Delay(double? seconds)
    {
        if (seconds is not { } value)
        {
            return "–";
        }

        var rounded = (int)Math.Round(value);
        var sign = rounded < 0 ? "−" : "+";
        var magnitude = Math.Abs(rounded);
        return magnitude < 60 ? $"{sign}{magnitude} s" : $"{sign}{magnitude / 60} min {magnitude % 60:00} s";
    }

    public static string Period(ReportPeriod period) =>
        period.From.Year == period.To.Year
            ? $"{period.From.ToString("MMM d", Culture)} – {period.To.ToString("MMM d, yyyy", Culture)}"
            : $"{period.From.ToString("MMM d, yyyy", Culture)} – {period.To.ToString("MMM d, yyyy", Culture)}";

    public static string Date(DateOnly date) => date.ToString("ddd MMM d, yyyy", Culture);

    public static string LocalTime(DateTimeOffset value, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(value, timeZone).ToString("h:mm tt", Culture);

    public static string Direction(int? directionId) =>
        directionId is { } id ? $"Direction {id}" : "–";
}
