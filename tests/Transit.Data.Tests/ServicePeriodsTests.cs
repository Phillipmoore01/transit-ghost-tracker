using Transit.Data.Reports;

namespace Transit.Data.Tests;

public class ServicePeriodsTests
{
    private static readonly TimeZoneInfo Boston = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Theory]
    [InlineData(2026, 6, 1, DayType.Weekday)]
    [InlineData(2026, 6, 5, DayType.Weekday)]
    [InlineData(2026, 6, 6, DayType.Saturday)]
    [InlineData(2026, 6, 7, DayType.Sunday)]
    public void Classifies_service_dates_by_day_of_week(int year, int month, int day, DayType expected)
    {
        Assert.Equal(expected, ServicePeriods.DayTypeOf(new DateOnly(year, month, day)));
    }

    [Theory]
    [InlineData(5, 59, TimeOfDay.Night)]
    [InlineData(6, 0, TimeOfDay.AmPeak)]
    [InlineData(8, 59, TimeOfDay.AmPeak)]
    [InlineData(9, 0, TimeOfDay.Midday)]
    [InlineData(15, 0, TimeOfDay.PmPeak)]
    [InlineData(19, 0, TimeOfDay.Evening)]
    [InlineData(22, 0, TimeOfDay.Night)]
    public void Classifies_trip_starts_by_local_time_of_day(int hour, int minute, TimeOfDay expected)
    {
        var localStart = new DateTimeOffset(2026, 6, 1, hour, minute, 0, TimeSpan.FromHours(-4));

        Assert.Equal(expected, ServicePeriods.TimeOfDayOf(localStart.ToUniversalTime(), Boston));
    }
}
