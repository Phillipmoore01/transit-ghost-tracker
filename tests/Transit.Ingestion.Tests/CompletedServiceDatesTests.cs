namespace Transit.Ingestion.Tests;

public class CompletedServiceDatesTests
{
    private static readonly TimeZoneInfo Boston = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeSpan ThirtyHours = TimeSpan.FromHours(30);

    [Fact]
    public void Includes_a_service_date_once_its_service_day_has_ended()
    {
        var now = new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.Zero);

        var dates = CompletedServiceDates.Within(now, Boston, ThirtyHours, lookbackDays: 3);

        Assert.Equal([new DateOnly(2026, 5, 31), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2)], dates);
    }

    [Fact]
    public void Excludes_a_service_date_whose_late_night_trips_may_still_be_running()
    {
        var now = new DateTimeOffset(2026, 6, 3, 9, 59, 59, TimeSpan.Zero);

        var dates = CompletedServiceDates.Within(now, Boston, ThirtyHours, lookbackDays: 3);

        Assert.Equal([new DateOnly(2026, 5, 31), new DateOnly(2026, 6, 1)], dates);
    }
}
