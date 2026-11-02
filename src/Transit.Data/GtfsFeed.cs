namespace Transit.Data;

public class GtfsFeed
{
    public int Id { get; set; }

    public required string Sha256 { get; set; }

    public required string SourceUrl { get; set; }

    public string? FeedVersion { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public DateTimeOffset ImportedAt { get; set; }
}
