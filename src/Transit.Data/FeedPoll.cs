namespace Transit.Data;

public class FeedPoll
{
    public long Id { get; set; }

    public DateTimeOffset PolledAt { get; set; }

    public DateTimeOffset FeedTimestamp { get; set; }

    public int EntityCount { get; set; }

    public int PositionsInserted { get; set; }
}
