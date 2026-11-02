namespace Transit.Data;

public class Agency
{
    public long Id { get; set; }

    public int FeedId { get; set; }

    public string? AgencyId { get; set; }

    public required string Name { get; set; }

    public required string TimeZone { get; set; }
}
