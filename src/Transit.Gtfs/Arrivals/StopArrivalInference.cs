namespace Transit.Gtfs.Arrivals;

public sealed record ScheduledStop(int StopSequence, string StopId, GtfsTime? ScheduledArrival);

public sealed record TripProgressReport(
    string VehicleId,
    DateTimeOffset ObservedAt,
    int StopSequence,
    VehicleStopStatus Status);

public enum ArrivalEvidence
{
    ReportedStopped = 0,
    InterpolatedBetweenReports = 1,
}

public sealed record InferredArrival(
    int StopSequence,
    string StopId,
    string VehicleId,
    DateTimeOffset ArrivedAt,
    ArrivalEvidence Evidence);

public static class StopArrivalInference
{
    public static IReadOnlyList<InferredArrival> Infer(
        IReadOnlyList<ScheduledStop> scheduledStops,
        IEnumerable<TripProgressReport> reports,
        TimeSpan maxInterpolationGap)
    {
        var stops = scheduledStops.OrderBy(s => s.StopSequence).ToArray();
        var scheduleAxis = BuildScheduleAxis(stops);
        if (scheduleAxis is null)
        {
            return [];
        }

        var indexBySequence = new Dictionary<int, int>(stops.Length);
        for (var i = 0; i < stops.Length; i++)
        {
            indexBySequence.TryAdd(stops[i].StopSequence, i);
        }

        var earliestByStop = new Dictionary<int, InferredArrival>();

        foreach (var vehicleReports in reports.GroupBy(r => r.VehicleId))
        {
            var progress = ForwardProgress(vehicleReports, indexBySequence);

            foreach (var arrival in InferForVehicle(stops, scheduleAxis, progress, maxInterpolationGap))
            {
                if (!earliestByStop.TryGetValue(arrival.StopSequence, out var existing)
                    || arrival.ArrivedAt < existing.ArrivedAt)
                {
                    earliestByStop[arrival.StopSequence] = arrival;
                }
            }
        }

        return earliestByStop.Values.OrderBy(a => a.StopSequence).ToArray();
    }

    private static IEnumerable<InferredArrival> InferForVehicle(
        ScheduledStop[] stops,
        double[] scheduleAxis,
        IReadOnlyList<Progress> progress,
        TimeSpan maxInterpolationGap)
    {
        if (progress.Count == 0)
        {
            yield break;
        }

        var cursor = 0;

        for (var stopIndex = 1; stopIndex < stops.Length; stopIndex++)
        {
            var stoppedRank = Progress.StoppedRank(stopIndex);

            while (cursor < progress.Count && progress[cursor].Rank < stoppedRank)
            {
                cursor++;
            }

            if (cursor == progress.Count)
            {
                yield break;
            }

            var reached = progress[cursor];
            var stop = stops[stopIndex];

            if (reached.Rank == stoppedRank)
            {
                yield return new InferredArrival(
                    stop.StopSequence,
                    stop.StopId,
                    reached.Report.VehicleId,
                    reached.Report.ObservedAt,
                    ArrivalEvidence.ReportedStopped);
                continue;
            }

            if (cursor == 0)
            {
                continue;
            }

            var before = progress[cursor - 1];
            var gap = reached.Report.ObservedAt - before.Report.ObservedAt;
            if (gap > maxInterpolationGap)
            {
                continue;
            }

            var fromAxis = before.AxisPosition(scheduleAxis);
            var toAxis = reached.AxisPosition(scheduleAxis);
            var fraction = toAxis > fromAxis
                ? Math.Clamp((scheduleAxis[stopIndex] - fromAxis) / (toAxis - fromAxis), 0, 1)
                : 0.5;

            yield return new InferredArrival(
                stop.StopSequence,
                stop.StopId,
                reached.Report.VehicleId,
                before.Report.ObservedAt + gap * fraction,
                ArrivalEvidence.InterpolatedBetweenReports);
        }
    }

    private static List<Progress> ForwardProgress(
        IEnumerable<TripProgressReport> reports,
        Dictionary<int, int> indexBySequence)
    {
        var progress = new List<Progress>();

        foreach (var report in reports.OrderBy(r => r.ObservedAt))
        {
            if (!indexBySequence.TryGetValue(report.StopSequence, out var stopIndex))
            {
                continue;
            }

            var rank = report.Status == VehicleStopStatus.StoppedAt
                ? Progress.StoppedRank(stopIndex)
                : Progress.ApproachingRank(stopIndex);

            if (progress.Count > 0
                && (rank < progress[^1].Rank || report.ObservedAt == progress[^1].Report.ObservedAt))
            {
                continue;
            }

            progress.Add(new Progress(report, stopIndex, rank));
        }

        return progress;
    }

    private static double[]? BuildScheduleAxis(ScheduledStop[] stops)
    {
        var axis = new double[stops.Length];
        var known = new List<int>();

        for (var i = 0; i < stops.Length; i++)
        {
            if (stops[i].ScheduledArrival is { } time)
            {
                axis[i] = time.SinceServiceDayStart.TotalSeconds;
                known.Add(i);
            }
        }

        if (known.Count < 2)
        {
            return null;
        }

        for (var k = 0; k < known.Count - 1; k++)
        {
            var (left, right) = (known[k], known[k + 1]);
            for (var i = left + 1; i < right; i++)
            {
                axis[i] = axis[left] + (axis[right] - axis[left]) * (i - left) / (right - left);
            }
        }

        for (var i = 0; i < known[0]; i++)
        {
            axis[i] = axis[known[0]];
        }

        for (var i = known[^1] + 1; i < stops.Length; i++)
        {
            axis[i] = axis[known[^1]];
        }

        return axis;
    }

    private readonly record struct Progress(TripProgressReport Report, int StopIndex, int Rank)
    {
        public static int ApproachingRank(int stopIndex) => 2 * stopIndex;

        public static int StoppedRank(int stopIndex) => 2 * stopIndex + 1;

        public double AxisPosition(double[] scheduleAxis) =>
            Rank == StoppedRank(StopIndex) || StopIndex == 0
                ? scheduleAxis[StopIndex]
                : (scheduleAxis[StopIndex - 1] + scheduleAxis[StopIndex]) / 2;
    }
}
