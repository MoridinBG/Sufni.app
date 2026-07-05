using System.Diagnostics;

namespace Sufni.Telemetry;

public static partial class TelemetryStatistics
{
    public static bool HasVibrationData(TelemetryData telemetryData, ImuLocation location)
    {
        return telemetryData.ImuData is { ActiveLocations.Count: > 0 } &&
            telemetryData.ImuData.HasSamples &&
            telemetryData.ImuData.ActiveLocations.Contains((byte)location);
    }

    public static VibrationStats? CalculateVibration(
        TelemetryData telemetryData,
        ImuLocation location,
        SuspensionType pairedSuspension,
        TelemetryTimeRange? range = null)
    {
        if (!HasVibrationData(telemetryData, location))
        {
            return null;
        }

        Debug.Assert(telemetryData.ImuData is not null);
        var suspension = GetSuspension(telemetryData, pairedSuspension);
        // Only divert to the segmented path when there is an actual gap. SST3/SST4 (and dense SST5)
        // populate one dense IMU segment per location with HasGaps = false, and must keep the dense
        // path so their vibration numbers do not change.
        if (suspension.HasGaps || telemetryData.ImuData.HasGaps)
        {
            return CalculateSegmentedVibration(telemetryData, location, suspension, range);
        }

        if (!HasStrokeData(telemetryData, pairedSuspension, range))
        {
            return null;
        }

        if (suspension.MaxTravel is not > 0 ||
            suspension.Travel.Length < 2 ||
            telemetryData.Metadata.SampleRate <= 0 ||
            telemetryData.ImuData.SampleRate <= 0)
        {
            return null;
        }

        var meta = telemetryData.ImuData.Meta.FirstOrDefault(entry => entry.LocationId == (byte)location);
        if (meta is null || meta.AccelLsbPerG <= 0)
        {
            return null;
        }

        if (!TryGetSampleRange(telemetryData, suspension, range, out var sampleRange) || sampleRange.Count < 2)
        {
            return null;
        }

        var selectedStartSeconds = range is null ? 0.0 : sampleRange.Start / (double)telemetryData.Metadata.SampleRate;
        var selectedEndSeconds = range is null
            ? double.PositiveInfinity
            : (sampleRange.End + 1) / (double)telemetryData.Metadata.SampleRate;

        var accelUp = new List<double>();
        var locationCount = telemetryData.ImuData.ActiveLocations.Count;
        for (var index = 0; index < telemetryData.ImuData.Records.Count; index++)
        {
            var locationIndex = index % locationCount;
            if (telemetryData.ImuData.ActiveLocations[locationIndex] != (byte)location)
            {
                continue;
            }

            var record = telemetryData.ImuData.Records[index];
            accelUp.Add(Math.Abs(record.Az / (double)meta.AccelLsbPerG - 1.0));
        }

        if (accelUp.Count == 0)
        {
            return null;
        }

        var totalMovement = 0.0;
        for (var index = sampleRange.Start + 1; index <= sampleRange.End; index++)
        {
            totalMovement += Math.Abs(suspension.Travel[index] - suspension.Travel[index - 1]);
        }

        var strokeKinds = Enumerable.Repeat(StrokeKind.Other, suspension.Travel.Length).ToArray();
        foreach (var stroke in GetIncludedCompressions(telemetryData, suspension, range))
        {
            var start = Math.Clamp(stroke.Start, 0, strokeKinds.Length - 1);
            var end = Math.Clamp(stroke.End, 0, strokeKinds.Length - 1);
            for (var index = start; index <= end; index++)
            {
                strokeKinds[index] = StrokeKind.Compression;
            }
        }

        foreach (var stroke in GetIncludedRebounds(telemetryData, suspension, range))
        {
            var start = Math.Clamp(stroke.Start, 0, strokeKinds.Length - 1);
            var end = Math.Clamp(stroke.End, 0, strokeKinds.Length - 1);
            for (var index = start; index <= end; index++)
            {
                strokeKinds[index] = StrokeKind.Rebound;
            }
        }

        var compression = new VibrationAccumulator();
        var rebound = new VibrationAccumulator();
        var other = new VibrationAccumulator();
        var overall = new VibrationAccumulator();

        for (var index = 0; index < accelUp.Count; index++)
        {
            var sampleTime = index / (double)telemetryData.ImuData.SampleRate;
            if (sampleTime < selectedStartSeconds || sampleTime >= selectedEndSeconds)
            {
                continue;
            }

            var suspensionIndex = Math.Clamp(
                (int)(sampleTime * telemetryData.Metadata.SampleRate),
                0,
                suspension.Travel.Length - 1);
            var positionRatio = suspension.Travel[suspensionIndex] / suspension.MaxTravel.Value;
            var g = accelUp[index];
            var kind = strokeKinds[suspensionIndex];

            overall.Add(g, positionRatio);
            switch (kind)
            {
                case StrokeKind.Compression:
                    compression.Add(g, positionRatio);
                    break;
                case StrokeKind.Rebound:
                    rebound.Add(g, positionRatio);
                    break;
                default:
                    other.Add(g, positionRatio);
                    break;
            }
        }

        if (overall.SumG <= 0)
        {
            return null;
        }

        var totalGSeconds = overall.SumG / telemetryData.ImuData.SampleRate;
        return new VibrationStats(
            compression.SumG / overall.SumG * 100.0,
            rebound.SumG / overall.SumG * 100.0,
            other.SumG / overall.SumG * 100.0,
            totalMovement / totalGSeconds,
            compression.AverageG,
            rebound.AverageG,
            overall.AverageG,
            compression.Thirds,
            rebound.Thirds,
            overall.Thirds);
    }

    private static VibrationStats? CalculateSegmentedVibration(
        TelemetryData telemetryData,
        ImuLocation location,
        Suspension suspension,
        TelemetryTimeRange? range)
    {
        Debug.Assert(telemetryData.ImuData is not null);
        if (suspension.MaxTravel is not > 0 ||
            suspension.Segments.Length == 0 ||
            telemetryData.Metadata.SampleRate <= 0 ||
            telemetryData.ImuData.SampleRate <= 0)
        {
            return null;
        }

        var meta = telemetryData.ImuData.Meta.FirstOrDefault(entry => entry.LocationId == (byte)location);
        if (meta is null || meta.AccelLsbPerG <= 0)
        {
            return null;
        }

        if (!TryGetSelectedSeconds(telemetryData, range, out var selectedStartSeconds, out var selectedEndSeconds) ||
            !HasStrokeInTimeRange(suspension, selectedStartSeconds, selectedEndSeconds, telemetryData.Metadata.SampleRate))
        {
            return null;
        }

        var sampler = new SuspensionTimeSeriesSampler(suspension.Segments, suspension.Travel, telemetryData.Metadata.SampleRate);
        var compression = new VibrationAccumulator();
        var rebound = new VibrationAccumulator();
        var other = new VibrationAccumulator();
        var overall = new VibrationAccumulator();

        foreach (var sample in EnumerateImuSamples(telemetryData.ImuData, location))
        {
            if (sample.Time < selectedStartSeconds || sample.Time >= selectedEndSeconds)
            {
                continue;
            }

            if (!sampler.TrySampleTravel(sample.Time, out var travel))
            {
                continue;
            }

            var positionRatio = travel / suspension.MaxTravel.Value;
            var g = Math.Abs(sample.Record.Az / (double)meta.AccelLsbPerG - 1.0);
            var kind = StrokeKindAtTime(suspension, sample.Time, telemetryData.Metadata.SampleRate);

            overall.Add(g, positionRatio);
            switch (kind)
            {
                case StrokeKind.Compression:
                    compression.Add(g, positionRatio);
                    break;
                case StrokeKind.Rebound:
                    rebound.Add(g, positionRatio);
                    break;
                default:
                    other.Add(g, positionRatio);
                    break;
            }
        }

        if (overall.SumG <= 0)
        {
            return null;
        }

        var totalMovement = CalculateSegmentedMovement(
            suspension,
            telemetryData.Metadata.SampleRate,
            selectedStartSeconds,
            selectedEndSeconds);
        var totalGSeconds = overall.SumG / telemetryData.ImuData.SampleRate;
        return new VibrationStats(
            compression.SumG / overall.SumG * 100.0,
            rebound.SumG / overall.SumG * 100.0,
            other.SumG / overall.SumG * 100.0,
            totalMovement / totalGSeconds,
            compression.AverageG,
            rebound.AverageG,
            overall.AverageG,
            compression.Thirds,
            rebound.Thirds,
            overall.Thirds);
    }

    private static bool TryGetSelectedSeconds(
        TelemetryData telemetryData,
        TelemetryTimeRange? range,
        out double selectedStartSeconds,
        out double selectedEndSeconds)
    {
        if (range is null)
        {
            selectedStartSeconds = 0.0;
            selectedEndSeconds = double.PositiveInfinity;
            return true;
        }

        selectedStartSeconds = Math.Clamp(range.Value.StartSeconds, 0, telemetryData.Metadata.Duration);
        selectedEndSeconds = Math.Clamp(range.Value.EndSeconds, 0, telemetryData.Metadata.Duration);
        return selectedEndSeconds - selectedStartSeconds >= TelemetryTimeRange.MinimumDurationSeconds;
    }

    private static IEnumerable<TimedImuSample> EnumerateImuSamples(RawImuData imuData, ImuLocation location)
    {
        var locationId = (byte)location;
        if (imuData.Segments.Count > 0)
        {
            foreach (var segment in imuData.Segments
                         .Where(segment => segment.LocationId == locationId)
                         .OrderBy(segment => segment.FirstMonotonicDeltaUs))
            {
                var startSeconds = segment.FirstMonotonicDeltaUs / 1_000_000.0;
                for (var index = 0; index < segment.Records.Length; index++)
                {
                    yield return new TimedImuSample(
                        startSeconds + index / (double)imuData.SampleRate,
                        segment.Records[index]);
                }
            }

            yield break;
        }

        var locationCount = imuData.ActiveLocations.Count;
        var sampleIndex = 0;
        for (var recordIndex = 0; recordIndex < imuData.Records.Count; recordIndex++)
        {
            var recordLocation = imuData.ActiveLocations[recordIndex % locationCount];
            if (recordLocation != locationId)
            {
                continue;
            }

            yield return new TimedImuSample(sampleIndex / (double)imuData.SampleRate, imuData.Records[recordIndex]);
            sampleIndex++;
        }
    }

    private static bool HasStrokeInTimeRange(Suspension suspension, double selectedStartSeconds, double selectedEndSeconds, int sampleRate) =>
        suspension.Strokes.Compressions.Concat(suspension.Strokes.Rebounds).Any(stroke =>
            StrokeEndSeconds(stroke, sampleRate) >= selectedStartSeconds &&
            StrokeStartSeconds(stroke, sampleRate) < selectedEndSeconds);

    private static StrokeKind StrokeKindAtTime(Suspension suspension, double seconds, int sampleRate)
    {
        if (suspension.Strokes.Compressions.Any(stroke => ContainsTime(stroke, seconds, sampleRate)))
        {
            return StrokeKind.Compression;
        }

        return suspension.Strokes.Rebounds.Any(stroke => ContainsTime(stroke, seconds, sampleRate))
            ? StrokeKind.Rebound
            : StrokeKind.Other;
    }

    private static bool ContainsTime(Stroke stroke, double seconds, int sampleRate) =>
        seconds >= StrokeStartSeconds(stroke, sampleRate) && seconds <= StrokeEndSeconds(stroke, sampleRate);

    private static double CalculateSegmentedMovement(
        Suspension suspension,
        int sampleRate,
        double selectedStartSeconds,
        double selectedEndSeconds)
    {
        var total = 0.0;
        foreach (var segment in suspension.Segments)
        {
            for (var index = 1; index < segment.SampleCount; index++)
            {
                var seconds = segment.StartSeconds + index / (double)sampleRate;
                if (seconds < selectedStartSeconds || seconds >= selectedEndSeconds)
                {
                    continue;
                }

                var denseIndex = segment.FirstDenseIndex + index;
                var previousDenseIndex = denseIndex - 1;
                if (previousDenseIndex < 0 || denseIndex >= suspension.Travel.Length)
                {
                    continue;
                }

                total += Math.Abs(suspension.Travel[denseIndex] - suspension.Travel[previousDenseIndex]);
            }
        }

        return total;
    }

    private readonly record struct TimedImuSample(double Time, ImuRecord Record);

    private sealed class VibrationAccumulator
    {
        private readonly double[] thirds = new double[3];

        public int Count { get; private set; }
        public double SumG { get; private set; }
        public double AverageG => Count == 0 ? 0 : SumG / Count;

        public StrokeThirds Thirds
        {
            get
            {
                if (SumG <= 0)
                {
                    return new StrokeThirds(0, 0, 0);
                }

                return new StrokeThirds(
                    thirds[0] / SumG * 100.0,
                    thirds[1] / SumG * 100.0,
                    thirds[2] / SumG * 100.0);
            }
        }

        public void Add(double g, double positionRatio)
        {
            Count++;
            SumG += g;

            var third = positionRatio switch
            {
                < 1.0 / 3.0 => 0,
                < 2.0 / 3.0 => 1,
                _ => 2,
            };
            thirds[third] += g;
        }
    }
}
