using Transit.Gtfs.Arrivals;

namespace Transit.Gtfs.Tests;

public class StopArrivalInferenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(3);

    private static readonly ScheduledStop[] FourStops =
    [
        Stop(1, "A", "08:00:00"),
        Stop(2, "B", "08:02:00"),
        Stop(3, "C", "08:04:00"),
        Stop(4, "D", "08:06:00"),
    ];

    [Fact]
    public void Uses_the_first_stopped_report_as_the_arrival()
    {
        var arrivals = Infer(
            FourStops,
            Report(0, 2, VehicleStopStatus.InTransitTo),
            Report(20, 2, VehicleStopStatus.StoppedAt),
            Report(40, 2, VehicleStopStatus.StoppedAt));

        var arrival = Assert.Single(arrivals);
        Assert.Equal(2, arrival.StopSequence);
        Assert.Equal("B", arrival.StopId);
        Assert.Equal(Start.AddSeconds(20), arrival.ArrivedAt);
        Assert.Equal(ArrivalEvidence.ReportedStopped, arrival.Evidence);
    }

    [Fact]
    public void Interpolates_a_stop_passed_between_two_reports()
    {
        var arrivals = Infer(
            FourStops,
            Report(0, 2, VehicleStopStatus.StoppedAt),
            Report(60, 3, VehicleStopStatus.InTransitTo),
            Report(120, 4, VehicleStopStatus.InTransitTo));

        var stopC = Assert.Single(arrivals, a => a.StopSequence == 3);
        Assert.Equal(ArrivalEvidence.InterpolatedBetweenReports, stopC.Evidence);
        Assert.Equal(Start.AddSeconds(90), stopC.ArrivedAt);
    }

    [Fact]
    public void Interpolates_several_stops_skipped_within_one_gap()
    {
        var arrivals = Infer(
            FourStops,
            Report(0, 1, VehicleStopStatus.StoppedAt),
            Report(120, 4, VehicleStopStatus.StoppedAt));

        Assert.Collection(
            arrivals,
            b =>
            {
                Assert.Equal(2, b.StopSequence);
                Assert.Equal(Start.AddSeconds(40), b.ArrivedAt);
            },
            c =>
            {
                Assert.Equal(3, c.StopSequence);
                Assert.Equal(Start.AddSeconds(80), c.ArrivedAt);
            },
            d =>
            {
                Assert.Equal(4, d.StopSequence);
                Assert.Equal(ArrivalEvidence.ReportedStopped, d.Evidence);
            });
    }

    [Fact]
    public void Does_not_interpolate_across_a_gap_longer_than_the_limit()
    {
        var arrivals = Infer(
            FourStops,
            Report(0, 2, VehicleStopStatus.InTransitTo),
            Report(600, 4, VehicleStopStatus.InTransitTo));

        Assert.Empty(arrivals);
    }

    [Fact]
    public void Does_not_report_the_first_stop_or_stops_passed_before_the_first_report()
    {
        var arrivals = Infer(
            FourStops,
            Report(0, 1, VehicleStopStatus.StoppedAt),
            Report(200, 3, VehicleStopStatus.StoppedAt));

        Assert.Equal([3], arrivals.Select(a => a.StopSequence));

        var lateJoin = Infer(FourStops, Report(0, 4, VehicleStopStatus.InTransitTo));

        Assert.Empty(lateJoin);
    }

    [Fact]
    public void Ignores_reports_that_move_backwards_along_the_trip()
    {
        var arrivals = Infer(
            FourStops,
            Report(0, 2, VehicleStopStatus.StoppedAt),
            Report(20, 3, VehicleStopStatus.InTransitTo),
            Report(40, 2, VehicleStopStatus.StoppedAt),
            Report(60, 3, VehicleStopStatus.StoppedAt));

        Assert.Equal(
            [(2, Start), (3, Start.AddSeconds(60))],
            arrivals.Select(a => (a.StopSequence, a.ArrivedAt)));
    }

    [Fact]
    public void Ignores_reports_for_stop_sequences_not_in_the_schedule()
    {
        var arrivals = Infer(
            FourStops,
            Report(0, 2, VehicleStopStatus.StoppedAt),
            Report(20, 99, VehicleStopStatus.StoppedAt));

        Assert.Equal([2], arrivals.Select(a => a.StopSequence));
    }

    [Fact]
    public void Keeps_the_earliest_arrival_when_several_vehicles_report_the_same_trip()
    {
        var arrivals = Infer(
            FourStops,
            Report(0, 2, VehicleStopStatus.StoppedAt, "y2"),
            Report(30, 2, VehicleStopStatus.StoppedAt, "y1"),
            Report(60, 3, VehicleStopStatus.StoppedAt, "y1"));

        Assert.Collection(
            arrivals,
            b => Assert.Equal(("y2", Start), (b.VehicleId, b.ArrivedAt)),
            c => Assert.Equal(("y1", Start.AddSeconds(60)), (c.VehicleId, c.ArrivedAt)));
    }

    [Fact]
    public void Fills_missing_scheduled_times_between_timepoints_for_interpolation()
    {
        ScheduledStop[] stops =
        [
            Stop(10, "A", "08:00:00"),
            Stop(20, "B", null),
            Stop(30, "C", "08:04:00"),
        ];

        var arrivals = Infer(
            stops,
            Report(0, 10, VehicleStopStatus.StoppedAt),
            Report(120, 30, VehicleStopStatus.StoppedAt));

        Assert.Equal(Start.AddSeconds(60), Assert.Single(arrivals, a => a.StopSequence == 20).ArrivedAt);
    }

    [Fact]
    public void Returns_nothing_when_the_schedule_has_fewer_than_two_timed_stops()
    {
        ScheduledStop[] stops = [Stop(1, "A", "08:00:00"), Stop(2, "B", null)];

        Assert.Empty(Infer(stops, Report(0, 2, VehicleStopStatus.StoppedAt)));
    }

    private static IReadOnlyList<InferredArrival> Infer(ScheduledStop[] stops, params TripProgressReport[] reports) =>
        StopArrivalInference.Infer(stops, reports, MaxGap);

    private static ScheduledStop Stop(int sequence, string stopId, string? arrival) =>
        new(sequence, stopId, arrival is null ? null : GtfsTime.Parse(arrival));

    private static TripProgressReport Report(
        int secondsAfterStart,
        int stopSequence,
        VehicleStopStatus status,
        string vehicleId = "y1") =>
        new(vehicleId, Start.AddSeconds(secondsAfterStart), stopSequence, status);
}
