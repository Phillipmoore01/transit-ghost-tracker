namespace Transit.Ingestion;

public class FeedHealthOptions
{
    public const string SectionName = "FeedHealth";

    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan MaxFeedGap { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan Lookback { get; set; } = TimeSpan.FromDays(1);

    public Uri? AlertWebhookUrl { get; set; }
}
