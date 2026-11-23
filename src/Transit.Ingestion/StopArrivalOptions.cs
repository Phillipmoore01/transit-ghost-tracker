namespace Transit.Ingestion;

public class StopArrivalOptions
{
    public const string SectionName = "StopArrivals";

    public TimeSpan RunInterval { get; set; } = TimeSpan.FromMinutes(30);

    public int LookbackDays { get; set; } = 3;

    public TimeSpan ServiceDayLength { get; set; } = TimeSpan.FromHours(30);

    public TimeSpan MaxInterpolationGap { get; set; } = TimeSpan.FromMinutes(3);
}
