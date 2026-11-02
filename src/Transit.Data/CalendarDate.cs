using Transit.Gtfs.Static;

namespace Transit.Data;

public class CalendarDate
{
    public int FeedId { get; set; }

    public required string ServiceId { get; set; }

    public DateOnly Date { get; set; }

    public CalendarExceptionType ExceptionType { get; set; }
}
