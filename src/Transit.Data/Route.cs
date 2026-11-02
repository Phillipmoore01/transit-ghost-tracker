namespace Transit.Data;

public class Route
{
    public int FeedId { get; set; }

    public required string RouteId { get; set; }

    public string? AgencyId { get; set; }

    public string? ShortName { get; set; }

    public string? LongName { get; set; }

    public int RouteType { get; set; }
}
