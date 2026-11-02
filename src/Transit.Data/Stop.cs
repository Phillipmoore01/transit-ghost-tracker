namespace Transit.Data;

public class Stop
{
    public int FeedId { get; set; }

    public required string StopId { get; set; }

    public string? Name { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public int LocationType { get; set; }

    public string? ParentStation { get; set; }
}
