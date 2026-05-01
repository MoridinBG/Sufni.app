using System.Diagnostics;

namespace Sufni.Telemetry;

public static partial class TelemetryStatistics
{
    public static bool HasVibrationData(TelemetryData telemetryData, ImuLocation location)
    {
        return telemetryData.ImuData is { Records.Count: > 0, ActiveLocations.Count: > 0 } &&
            telemetryData.ImuData.ActiveLocations.Contains((byte)location);
    }

    public static VibrationStats? CalculateVibration(
        TelemetryData telemetryData,
        ImuLocation location,
        SuspensionType pairedSuspension,
        TelemetryTimeRange? range = null)
    {
        if (!HasVibrationData(telemetryData, location) || !HasStrokeData(telemetryData, pairedSuspension, range))
        {
            return null;
        }

        Debug.Assert(telemetryData.ImuData is not null);
        var suspension = GetSuspension(telemetryData, pairedSuspension);
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
