namespace Transit.Gtfs.Tests;

public class GtfsTimeTests
{
    private static readonly TimeZoneInfo Boston = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Theory]
    [InlineData("08:05:30", 8, 5, 30)]
    [InlineData("8:05:30", 8, 5, 30)]
    [InlineData("25:10:00", 25, 10, 0)]
    [InlineData(" 00:00:00 ", 0, 0, 0)]
    public void Parses_times_including_past_midnight(string value, int hours, int minutes, int seconds)
    {
        Assert.Equal(new TimeSpan(hours, minutes, seconds), GtfsTime.Parse(value).SinceServiceDayStart);
    }

    [Theory]
    [InlineData("12:60:00")]
    [InlineData("12:00:60")]
    [InlineData("12:00")]
    [InlineData("-1:00:00")]
    [InlineData("ab:cd:ef")]
    [InlineData("")]
    public void Rejects_malformed_times(string value)
    {
        Assert.Throws<FormatException>(() => GtfsTime.Parse(value));
    }

    [Fact]
    public void Resolves_against_the_agency_time_zone()
    {
        var resolved = GtfsTime.Parse("08:00:00").ResolveOn(new DateOnly(2026, 6, 1), Boston);

        Assert.Equal(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero), resolved);
    }

    [Fact]
    public void Resolves_times_past_midnight_onto_the_next_calendar_day()
    {
        var resolved = GtfsTime.Parse("25:00:00").ResolveOn(new DateOnly(2026, 6, 1), Boston);

        Assert.Equal(new DateTimeOffset(2026, 6, 2, 5, 0, 0, TimeSpan.Zero), resolved);
    }

    [Fact]
    public void Measures_from_noon_minus_twelve_hours_on_daylight_saving_start()
    {
        var serviceDate = new DateOnly(2026, 3, 8);

        Assert.Equal(
            new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero),
            GtfsTime.Parse("01:00:00").ResolveOn(serviceDate, Boston));
        Assert.Equal(
            new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.Zero),
            GtfsTime.Parse("08:00:00").ResolveOn(serviceDate, Boston));
    }
}
