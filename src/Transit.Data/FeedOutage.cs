namespace Transit.Data;

public class FeedOutage
{
    public long Id { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public DateTimeOffset DetectedAt { get; set; }
}
