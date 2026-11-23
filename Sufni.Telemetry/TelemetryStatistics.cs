using System.Collections.Immutable;
namespace Sufni.Telemetry;

public static partial class TelemetryStatistics
{
    private enum StrokeKind
    {
        Other,
        Compression,
        Rebound,
    }

    private static Suspension GetSuspension(TelemetryData telemetryData, SuspensionType type)
    {
        return type == SuspensionType.Front ? telemetryData.Front : telemetryData.Rear;
    }

    private static ImmutableArray<double> GetTravelSamples(
        TelemetryData telemetryData, Suspension suspension, TelemetryTimeSelection? selection)
    {
        if (telemetryData.Metadata.SampleRate <= 0 || suspension.Travel.IsDefaultOrEmpty) return [];
        if (selection is null) return suspension.Travel;
        var samples = ImmutableArray.CreateBuilder<double>();
        foreach (var interval in SuspensionSampleInterval.Select(suspension.Segments, suspension.Travel.Length,
                     telemetryData.Metadata.SampleRate, selection))
            samples.AddRange(suspension.Travel.AsSpan(interval.Start, interval.Count));
        return samples.ToImmutable();
    }

    private static ImmutableArray<Stroke> GetIncludedStrokes(
        TelemetryData telemetryData, ImmutableArray<Stroke>? source, TelemetryTimeSelection? selection)
    {
        if (source is not { IsDefaultOrEmpty: false } strokes || telemetryData.Metadata.SampleRate <= 0) return [];
        if (selection is null) return strokes;
        return strokes.Where(stroke => selection.Contains(StrokeStartSeconds(stroke, telemetryData.Metadata.SampleRate)))
            .ToImmutableArray();
    }

    private static ImmutableArray<Stroke> GetIncludedCompressions(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeSelection? range)
    {
        return GetIncludedStrokes(telemetryData, suspension.Strokes?.Compressions, range);
    }

    private static ImmutableArray<Stroke> GetIncludedRebounds(
        TelemetryData telemetryData,
        Suspension suspension,
        TelemetryTimeSelection? range)
    {
        return GetIncludedStrokes(telemetryData, suspension.Strokes?.Rebounds, range);
    }

    private static ImmutableArray<Stroke> GetIncludedStrokes(
        TelemetryData telemetryData,
        Suspension suspension,
        BalanceType strokeKind,
        TelemetryTimeSelection? range)
    {
        return strokeKind == BalanceType.Compression
            ? GetIncludedCompressions(telemetryData, suspension, range)
            : GetIncludedRebounds(telemetryData, suspension, range);
    }

    private static bool IncludesSample(TelemetryTimeSelection? selection, Stroke stroke, int offset, int rate) =>
        selection is null || selection.Contains(stroke.EndSeconds > stroke.StartSeconds
            ? stroke.StartSeconds + (double)offset / rate : (double)(stroke.Start + offset) / rate);

    private static double StrokeStartSeconds(Stroke stroke, int sampleRate) =>
        stroke.EndSeconds > stroke.StartSeconds
            ? stroke.StartSeconds
            : sampleRate > 0 ? stroke.Start / (double)sampleRate : stroke.Start;

    private static double StrokeEndSeconds(Stroke stroke, int sampleRate) =>
        stroke.EndSeconds > stroke.StartSeconds
            ? stroke.EndSeconds
            : sampleRate > 0 ? stroke.End / (double)sampleRate : stroke.End;
}
