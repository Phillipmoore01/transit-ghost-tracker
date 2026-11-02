using System.IO.Compression;
using System.Text;
using Transit.Gtfs.Static;

namespace Transit.Gtfs.Tests;

public class GtfsStaticFeedTests
{
    [Fact]
    public void Reads_quoted_fields_with_a_byte_order_mark_and_crlf_line_endings()
    {
        using var feed = Feed(("routes.txt",
            "﻿route_id,route_short_name,route_long_name,route_type\r\n" +
            "Red,,\"Red Line, Ashmont/Braintree\",1\r\n"));

        var route = Assert.Single(feed.ReadRoutes());

        Assert.Equal(new RouteRecord("Red", null, null, "Red Line, Ashmont/Braintree", 1), route);
    }

    [Fact]
    public void Matches_columns_by_header_name_and_ignores_unknown_columns()
    {
        using var feed = Feed(("trips.txt",
            "wheelchair_accessible,service_id,trip_id,route_id,trip_headsign\n" +
            "1,weekday,trip-1,42,Harvard\n"));

        var trip = Assert.Single(feed.ReadTrips());

        Assert.Equal(new TripRecord("trip-1", "42", "weekday", null, "Harvard", null, null), trip);
    }

    [Fact]
    public void Reads_stop_times_past_midnight_and_without_arrival_times()
    {
        using var feed = Feed(("stop_times.txt",
            "trip_id,arrival_time,departure_time,stop_id,stop_sequence\n" +
            "trip-1,25:10:00,25:11:00,place-sstat,1\n" +
            "trip-1,,,place-dwnxg,2\n"));

        Assert.Collection(
            feed.ReadStopTimes(),
            first =>
            {
                Assert.Equal(new TimeSpan(25, 10, 0), first.ArrivalTime?.SinceServiceDayStart);
                Assert.Equal(new TimeSpan(25, 11, 0), first.DepartureTime?.SinceServiceDayStart);
            },
            second =>
            {
                Assert.Null(second.ArrivalTime);
                Assert.Null(second.DepartureTime);
                Assert.Equal(2, second.StopSequence);
            });
    }

    [Fact]
    public void Reads_service_calendars_and_exceptions()
    {
        using var feed = Feed(
            ("calendar.txt",
                "service_id,monday,tuesday,wednesday,thursday,friday,saturday,sunday,start_date,end_date\n" +
                "weekday,1,1,1,1,1,0,0,20261023,20261211\n"),
            ("calendar_dates.txt",
                "service_id,date,exception_type\n" +
                "weekday,20261126,2\n" +
                "holiday,20261126,1\n"));

        Assert.Equal(
            new CalendarRecord("weekday", true, true, true, true, true, false, false, new DateOnly(2026, 10, 23), new DateOnly(2026, 12, 11)),
            Assert.Single(feed.ReadCalendar()));
        Assert.Equal(
            [
                new CalendarDateRecord("weekday", new DateOnly(2026, 11, 26), CalendarExceptionType.Removed),
                new CalendarDateRecord("holiday", new DateOnly(2026, 11, 26), CalendarExceptionType.Added),
            ],
            feed.ReadCalendarDates());
    }

    [Fact]
    public void Reads_feed_info_when_present()
    {
        using var feed = Feed(("feed_info.txt",
            "feed_publisher_name,feed_version,feed_start_date,feed_end_date\n" +
            "MBTA,\"Fall 2026, version D\",20261023,20261212\n"));

        Assert.Equal(
            new FeedInfoRecord("Fall 2026, version D", new DateOnly(2026, 10, 23), new DateOnly(2026, 12, 12)),
            feed.ReadFeedInfo());
    }

    [Fact]
    public void Treats_missing_optional_files_as_empty()
    {
        using var feed = Feed();

        Assert.Null(feed.ReadFeedInfo());
        Assert.Empty(feed.ReadCalendar());
        Assert.Empty(feed.ReadCalendarDates());
    }

    [Fact]
    public void Rejects_a_feed_without_a_required_file()
    {
        using var feed = Feed();

        var error = Assert.Throws<FormatException>(() => feed.ReadStopTimes().ToList());

        Assert.Contains("stop_times.txt", error.Message);
    }

    [Theory]
    [InlineData("route_id,route_type\nRed,subway\n", "routes.txt row 2: field 'route_type' value 'subway' is not an integer.")]
    [InlineData("route_id,route_type\nRed,1\n,3\n", "routes.txt row 3: required field 'route_id' is missing or empty.")]
    public void Reports_the_file_and_row_of_an_invalid_value(string content, string expectedMessage)
    {
        using var feed = Feed(("routes.txt", content));

        var error = Assert.Throws<FormatException>(() => feed.ReadRoutes().ToList());

        Assert.Equal(expectedMessage, error.Message);
    }

    [Fact]
    public void Rejects_an_invalid_stop_time()
    {
        using var feed = Feed(("stop_times.txt",
            "trip_id,arrival_time,departure_time,stop_id,stop_sequence\n" +
            "trip-1,8:61:00,8:61:00,place-sstat,1\n"));

        var error = Assert.Throws<FormatException>(() => feed.ReadStopTimes().ToList());

        Assert.StartsWith("stop_times.txt row 2:", error.Message);
    }

    private static GtfsStaticFeed Feed(params (string Name, string Content)[] files)
    {
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        zip.Position = 0;
        return GtfsStaticFeed.Open(zip);
    }
}
