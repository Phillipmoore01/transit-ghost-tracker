namespace Transit.Data;

public class ServiceCalendar
{
    public int FeedId { get; set; }

    public required string ServiceId { get; set; }

    public bool Monday { get; set; }

    public bool Tuesday { get; set; }

    public bool Wednesday { get; set; }

    public bool Thursday { get; set; }

    public bool Friday { get; set; }

    public bool Saturday { get; set; }

    public bool Sunday { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }
}
