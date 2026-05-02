namespace Sufni.Telemetry;

public static partial class TelemetryStatistics
{
    private enum StrokeKind
    {
        Other,
        Compression,
        Rebound,
    }

    private readonly record struct SampleRange(int Start, int End)
    {
        public int Count => End - Start + 1;

        public bool Contains(Stroke stroke)
        {
            return stroke.Start >= Start && stroke.End <= End;
        }
    }

    private static Suspension GetSuspension(TelemetryData telemetryData, SuspensionType type)
    {
        return type == SuspensionType.Front ? telemetryData.Front : telemetryData.Rear;
    }

    private static bool TryGetSampleRange(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeRange? range,
        out SampleRange sampleRange)
    {
        sampleRange = default;
        if (suspension.Travel.Length == 0 || telemetryData.Metadata.SampleRate <= 0)
        {
            return false;
        }

        if (range is null)
        {
            sampleRange = new SampleRange(0, suspension.Travel.Length - 1);
            return true;
        }

        var startSeconds = Math.Clamp(range.Value.StartSeconds, 0, telemetryData.Metadata.Duration);
        var endSeconds = Math.Clamp(range.Value.EndSeconds, 0, telemetryData.Metadata.Duration);
        if (endSeconds - startSeconds < TelemetryTimeRange.MinimumDurationSeconds)
        {
            return false;
        }

        var startIndex = (int)Math.Floor(startSeconds * telemetryData.Metadata.SampleRate);
        var endIndex = (int)Math.Ceiling(endSeconds * telemetryData.Metadata.SampleRate) - 1;
        startIndex = Math.Clamp(startIndex, 0, suspension.Travel.Length - 1);
        endIndex = Math.Clamp(endIndex, 0, suspension.Travel.Length - 1);
        if (endIndex < startIndex)
        {
            return false;
        }

        sampleRange = new SampleRange(startIndex, endIndex);
        return true;
    }

    private static double[] GetTravelSamples(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeRange? range)
    {
        if (!TryGetSampleRange(telemetryData, suspension, range, out var sampleRange))
        {
            return [];
        }

        return range is null
            ? suspension.Travel
            : suspension.Travel[sampleRange.Start..(sampleRange.End + 1)];
    }

    private static Stroke[] GetIncludedStrokes(
        TelemetryData telemetryData,
        Suspension suspension,
        Stroke[]? strokes,
        TelemetryTimeRange? range)
    {
        if (strokes is null || strokes.Length == 0 ||
            !TryGetSampleRange(telemetryData, suspension, range, out var sampleRange))
        {
            return [];
        }

        return range is null
            ? strokes
            : strokes.Where(sampleRange.Contains).ToArray();
    }

    private static Stroke[] GetIncludedCompressions(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeRange? range)
    {
        return GetIncludedStrokes(telemetryData, suspension, suspension.Strokes?.Compressions, range);
    }

    private static Stroke[] GetIncludedRebounds(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeRange? range)
    {
        return GetIncludedStrokes(telemetryData, suspension, suspension.Strokes?.Rebounds, range);
    }

    private static Stroke[] GetIncludedStrokes(
        TelemetryData telemetryData,
        Suspension suspension,
        BalanceType strokeKind,
        TelemetryTimeRange? range)
    {
        return strokeKind == BalanceType.Compression
            ? GetIncludedCompressions(telemetryData, suspension, range)
            : GetIncludedRebounds(telemetryData, suspension, range);
    }
}
