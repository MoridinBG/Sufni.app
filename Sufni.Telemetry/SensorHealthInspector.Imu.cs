namespace Sufni.Telemetry;

public static partial class SensorHealthInspector
{
    private sealed class ImuSeconds(int seconds)
    {
        public readonly int[] Samples = new int[seconds];
        public readonly bool[] NonZero = new bool[seconds];
        public readonly double[] Sum = new double[seconds];
        public readonly double[] SumOfSquares = new double[seconds];
    }

    private static void InspectImu(RawTelemetryData raw, bool[] moving, List<SensorFinding> findings)
    {
        if (raw.ImuData is not { HasSamples: true, SampleRate: > 0 } imu) return;
        var seconds = moving.Length;
        var locations = new SortedDictionary<byte, ImuSeconds>();
        foreach (var segment in imu.SampleSegments)
        {
            if (!locations.TryGetValue(segment.LocationId, out var measured))
                locations[segment.LocationId] = measured = new ImuSeconds(seconds);
            var lsbPerG = imu.Meta.FirstOrDefault(meta => meta.LocationId == segment.LocationId)?.AccelLsbPerG ?? 0;
            var start = segment.FirstMonotonicDeltaUs / 1e6;
            for (var i = 0; i < segment.Count; i++)
            {
                var k = SecondOf(start + i / (double)imu.SampleRate, seconds);
                if (k < 0) continue;
                var record = segment[i];
                measured.Samples[k]++;
                measured.NonZero[k] |= record != default;
                if (lsbPerG <= 0) continue;
                var magnitude = Math.Sqrt((double)record.Ax * record.Ax + (double)record.Ay * record.Ay +
                    (double)record.Az * record.Az) / lsbPerG;
                measured.Sum[k] += magnitude;
                measured.SumOfSquares[k] += magnitude * magnitude;
            }
        }

        foreach (var (location, measured) in locations)
        {
            var zero = new bool[seconds];
            var frozen = new bool[seconds];
            for (var k = 0; k < seconds; k++)
            {
                var n = measured.Samples[k];
                zero[k] = n > 0 && !measured.NonZero[k];
                if (zero[k] || n < 2 || !moving[k]) continue;
                var mean = measured.Sum[k] / n;
                var variance = Math.Max(0, measured.SumOfSquares[k] / n - mean * mean);
                frozen[k] = mean > 0 && Math.Sqrt(variance) < SensorHealthParameters.ImuFrozenMaximumStdDevG;
            }
            var sensor = new SensorIdentity(SensorKind.Imu, location);
            Add(findings, sensor, SensorFaultReason.Zero, SecondRanges(zero));
            Add(findings, sensor, SensorFaultReason.Frozen, SecondRanges(frozen));
        }
    }
}
