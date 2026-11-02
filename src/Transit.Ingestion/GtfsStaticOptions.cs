namespace Transit.Ingestion;

public class GtfsStaticOptions
{
    public const string SectionName = "GtfsStatic";

    public required Uri ScheduleUrl { get; set; }

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(6);
}
