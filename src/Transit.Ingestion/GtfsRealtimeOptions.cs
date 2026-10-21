namespace Transit.Ingestion;

public class GtfsRealtimeOptions
{
    public const string SectionName = "GtfsRealtime";

    public required Uri VehiclePositionsUrl { get; set; }

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(20);
}
