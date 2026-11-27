namespace Sufni.Telemetry;

public static partial class SensorHealthInspector
{
    private const int CircularCounts = 4096;

    private sealed record TravelSide(SensorIdentity Sensor, double[] Activity, double[] Median, int MaxTravelCount, bool Wraps);

    /// <returns>For each second, whether the bike moves.</returns>
    private static bool[] InspectTravel(RawTelemetryData raw, BikeData bike, int seconds, List<SensorFinding> findings)
    {
        var front = MeasureSide(new SensorIdentity(SensorKind.ForkTravel), raw.FrontSegments, raw.Front, raw.SampleRate,
            bike.FrontMaxTravel, bike.FrontMeasurementToTravel, bike.FrontMaxTravelCount, bike.FrontMeasurementWraps, seconds);
        var rear = MeasureSide(new SensorIdentity(SensorKind.ShockTravel), raw.RearSegments, raw.Rear, raw.SampleRate,
            bike.RearMaxTravel, bike.RearMeasurementToTravel, bike.RearMaxTravelCount, bike.RearMeasurementWraps, seconds);
        TravelSide[] sides = [.. new[] { front, rear }.OfType<TravelSide>()];

        foreach (var side in sides)
        {
            var bad = new bool[seconds];
            for (var k = 0; k < seconds; k++)
                bad[k] = !double.IsNaN(side.Median[k]) &&
                    DistanceToReach(side.Median[k], side.MaxTravelCount, side.Wraps) > SensorHealthParameters.TravelRangeToleranceCounts;
            Add(findings, side.Sensor, SensorFaultReason.OutOfRange, SecondRanges(bad));
        }

        if (front is not null && rear is not null)
        {
            Add(findings, front.Sensor, SensorFaultReason.Frozen, SecondRanges(Frozen(front, rear, seconds)));
            Add(findings, rear.Sensor, SensorFaultReason.Frozen, SecondRanges(Frozen(rear, front, seconds)));
        }

        var moving = new bool[seconds];
        for (var k = 0; k < seconds; k++) moving[k] = sides.Any(side => IsActive(side.Activity[k]));
        return moving;
    }

    private static bool IsActive(double activity) => activity >= SensorHealthParameters.TravelActiveMinimumSpanFractionPerSecond;

    private static bool[] Frozen(TravelSide side, TravelSide other, int seconds)
    {
        var bad = new bool[seconds];
        for (var k = 0; k < seconds; k++)
            bad[k] = IsActive(other.Activity[k]) && !double.IsNaN(side.Activity[k]) &&
                side.Activity[k] / other.Activity[k] < SensorHealthParameters.TravelFrozenMaximumActivityRatio;
        return bad;
    }

    private static double DistanceToReach(double median, int maxTravelCount, bool wraps)
    {
        static double Distance(double count, int max) => count < 0 ? -count : count > max ? count - max : 0;
        return wraps
            ? Math.Min(Distance(median, maxTravelCount),
                Math.Min(Distance(median + CircularCounts, maxTravelCount), Distance(median - CircularCounts, maxTravelCount)))
            : Distance(median, maxTravelCount);
    }

    /// <summary>The activity and the median count of each second, or null when the side is not inspected.</summary>
    private static TravelSide? MeasureSide(SensorIdentity sensor, RawCountSegment[] segments, ushort[] dense, int rate,
        double? maxTravel, Func<ushort, double>? measurementToTravel, ushort? maxTravelCount, bool wraps, int seconds)
    {
        IEnumerable<(double Start, ushort[] Counts)> runs = segments.Length > 0
            ? segments.Select(segment => (segment.FirstMonotonicDeltaUs / 1e6, segment.Counts))
            : [(0.0, dense)];
        if (rate <= 0 || maxTravelCount is not > 0 ||
            !TelemetryData.HasUsableTravel(runs.SelectMany(run => run.Counts), maxTravel, measurementToTravel)) return null;

        var stepSums = new double[seconds];
        var stepCounts = new int[seconds];
        var counts = new List<int>?[seconds];
        foreach (var (start, values) in runs)
        {
            var second = -1;
            var unwrapped = 0;
            for (var i = 0; i < values.Length; i++)
            {
                var k = SecondOf(start + i / (double)rate, seconds);
                if (k < 0) continue;
                var step = i == 0 ? 0 : Step(values[i - 1], values[i], wraps);
                if (i > 0)
                {
                    stepSums[k] += Math.Abs(step);
                    stepCounts[k]++;
                }
                unwrapped = k == second ? unwrapped + step : Normalize(values[i], wraps);
                second = k;
                (counts[k] ??= []).Add(unwrapped);
            }
        }

        var activity = new double[seconds];
        var median = new double[seconds];
        for (var k = 0; k < seconds; k++)
        {
            activity[k] = stepCounts[k] == 0 ? double.NaN : stepSums[k] / stepCounts[k] * rate / maxTravelCount.Value;
            median[k] = counts[k] is { Count: > 0 } values ? Signed(Median(values), wraps) : double.NaN;
        }
        return new TravelSide(sensor, activity, median, maxTravelCount.Value, wraps);
    }

    private static int Normalize(ushort count, bool wraps) => wraps ? count % CircularCounts : count;

    private static int Step(ushort previous, ushort current, bool wraps)
    {
        var step = Normalize(current, wraps) - Normalize(previous, wraps);
        if (!wraps) return step;
        if (step >= CircularCounts / 2) step -= CircularCounts;
        else if (step < -CircularCounts / 2) step += CircularCounts;
        return step;
    }

    private static double Signed(double count, bool wraps)
    {
        if (!wraps) return count;
        var wrapped = (count % CircularCounts + CircularCounts) % CircularCounts;
        return wrapped >= CircularCounts / 2 ? wrapped - CircularCounts : wrapped;
    }

    private static double Median(List<int> values)
    {
        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2.0;
    }
}
