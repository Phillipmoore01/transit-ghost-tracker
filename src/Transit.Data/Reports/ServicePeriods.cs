namespace Transit.Data.Reports;

public static class ServicePeriods
{
    public static DayType DayTypeOf(DateOnly serviceDate) =>
        serviceDate.DayOfWeek switch
        {
            DayOfWeek.Saturday => DayType.Saturday,
            DayOfWeek.Sunday => DayType.Sunday,
            _ => DayType.Weekday,
        };

    public static TimeOfDay TimeOfDayOf(DateTimeOffset scheduledStart, TimeZoneInfo agencyTimeZone) =>
        TimeZoneInfo.ConvertTime(scheduledStart, agencyTimeZone).Hour switch
        {
            >= 6 and < 9 => TimeOfDay.AmPeak,
            >= 9 and < 15 => TimeOfDay.Midday,
            >= 15 and < 19 => TimeOfDay.PmPeak,
            >= 19 and < 22 => TimeOfDay.Evening,
            _ => TimeOfDay.Night,
        };

    public static string Describe(TimeOfDay timeOfDay) =>
        timeOfDay switch
        {
            TimeOfDay.AmPeak => "Morning peak (6–9)",
            TimeOfDay.Midday => "Midday (9–15)",
            TimeOfDay.PmPeak => "Evening peak (15–19)",
            TimeOfDay.Evening => "Evening (19–22)",
            _ => "Night (22–6)",
        };
}
