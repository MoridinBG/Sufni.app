namespace Sufni.Telemetry;

/// <summary>
/// Wheel position (% of max travel) against velocity (mm/s) for the compression and rebound
/// strokes of each side. Strokes are clipped to the analysis range.
/// </summary>
public sealed record PositionVelocityTraceSet(
    PositionVelocitySideTrace? Front,
    PositionVelocitySideTrace? Rear,
    double VelocityLimitMmPerSecond);

/// <param name="Heatmap">
/// Percent of the side's points per [velocity bin, position bin] over the symmetric velocity
/// limit of the trace set; empty bins are NaN.
/// </param>
public sealed record PositionVelocitySideTrace(
    IReadOnlyList<PositionVelocityStrokeTrace> Strokes,
    double[,] Heatmap);

public sealed record PositionVelocityStrokeTrace(
    double[] PositionPercent,
    double[] VelocityMmPerSecond)
{
    public int Count => Math.Min(PositionPercent.Length, VelocityMmPerSecond.Length);
}

public static class PositionVelocityTraceBuilder
{
    public const int HeatmapPositionBinCount = 100;
    public const int HeatmapVelocityBinCount = 100;

    private const int MaximumPointsPerSide = 20_000;
    private const int MaximumStrokeTracesPerSide = 800;
    private const double MinimumVelocityLimitMmPerSecond = 1_000;
    private const double VelocityLimitRoundingMmPerSecond = 500;

    public static PositionVelocityTraceSet Build(
        TelemetryData telemetry,
        TelemetryTimeRange? analysisRange)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        var sampleRate = telemetry.Metadata?.SampleRate ?? 0;
        IReadOnlyList<PositionVelocityStrokeTrace>? front = null;
        IReadOnlyList<PositionVelocityStrokeTrace>? rear = null;
        Parallel.Invoke(
            () => front = BuildSide(telemetry.Front, sampleRate, analysisRange),
            () => rear = BuildSide(telemetry.Rear, sampleRate, analysisRange));
        var velocityLimit = CalculateVelocityLimit(front, rear);

        return new PositionVelocityTraceSet(
            CreateSideTrace(front, velocityLimit),
            CreateSideTrace(rear, velocityLimit),
            velocityLimit);
    }

    private static PositionVelocitySideTrace? CreateSideTrace(
        IReadOnlyList<PositionVelocityStrokeTrace>? strokes,
        double velocityLimit)
    {
        return strokes is null
            ? null
            : new PositionVelocitySideTrace(strokes, BuildHeatmap(strokes, velocityLimit));
    }

    private static IReadOnlyList<PositionVelocityStrokeTrace>? BuildSide(
        Suspension? suspension,
        int sampleRate,
        TelemetryTimeRange? analysisRange)
    {
        if (suspension is null ||
            !suspension.Present ||
            suspension.MaxTravel is not { } maxTravel ||
            !double.IsFinite(maxTravel) ||
            maxTravel <= 0 ||
            suspension.Travel is not { IsDefault: false, Length: > 1 } travel ||
            suspension.Velocity is not { IsDefault: false, Length: > 1 } velocity ||
            suspension.Strokes is null)
        {
            return null;
        }

        var pairedCount = Math.Min(travel.Length, velocity.Length);
        if (pairedCount < 2)
        {
            return null;
        }

        var strokes = BuildCandidateStrokes(suspension.Strokes);
        if (strokes.Length == 0)
        {
            return null;
        }

        var sampleRanges = BuildSampleRanges(
            suspension.Segments,
            pairedCount,
            sampleRate,
            analysisRange);
        if (sampleRanges.Count == 0)
        {
            return null;
        }

        var strokePlans = BuildStrokePlans(
            strokes,
            travel,
            velocity,
            pairedCount,
            sampleRanges);
        if (strokePlans.Count == 0)
        {
            return null;
        }

        strokePlans = CapStrokeCount(strokePlans);
        var pointStride = CalculatePointStride(strokePlans);
        var strokeTraces = strokePlans
            .Select(plan => BuildStrokeTrace(plan, travel, velocity, maxTravel, pointStride))
            .ToList();
        return strokeTraces.Count == 0 ? null : strokeTraces;
    }

    private static Stroke[] BuildCandidateStrokes(Strokes strokes)
    {
        return (strokes.Compressions.IsDefault ? [] : strokes.Compressions)
            .Concat(strokes.Rebounds.IsDefault ? [] : strokes.Rebounds)
            .Where(stroke => stroke is not null)
            .OrderBy(stroke => stroke.Start)
            .ThenBy(stroke => stroke.End)
            .ToArray();
    }

    private static IReadOnlyList<SampleRange> BuildSampleRanges(
        IReadOnlyList<ProcessedSuspensionSegment> segments,
        int pairedCount,
        int sampleRate,
        TelemetryTimeRange? analysisRange)
    {
        if (segments.Count == 0)
        {
            return TryGetLegacySampleRange(
                pairedCount,
                sampleRate,
                analysisRange,
                out var startIndex,
                out var endExclusive)
                ? [new SampleRange(startIndex, endExclusive)]
                : [];
        }

        if (analysisRange is not null && sampleRate <= 0)
        {
            return [];
        }

        var ranges = new List<SampleRange>(segments.Count);
        foreach (var segment in segments)
        {
            if (segment.FirstDenseIndex < 0 ||
                segment.SampleCount <= 0 ||
                !double.IsFinite(segment.StartSeconds) ||
                segment.FirstDenseIndex >= pairedCount)
            {
                continue;
            }

            var availableSampleCount = Math.Min(
                segment.SampleCount,
                pairedCount - segment.FirstDenseIndex);
            var localStartIndex = 0;
            var localEndExclusive = availableSampleCount;
            if (analysisRange is { } range)
            {
                var segmentEndSeconds =
                    segment.StartSeconds + availableSampleCount / (double)sampleRate;
                var selectedStartSeconds = Math.Max(range.StartSeconds, segment.StartSeconds);
                var selectedEndSeconds = Math.Min(range.EndSeconds, segmentEndSeconds);
                if (selectedEndSeconds <= selectedStartSeconds)
                {
                    continue;
                }

                localStartIndex = (int)Math.Floor(
                    (selectedStartSeconds - segment.StartSeconds) * sampleRate);
                localEndExclusive = (int)Math.Ceiling(
                    (selectedEndSeconds - segment.StartSeconds) * sampleRate);
                localStartIndex = Math.Clamp(localStartIndex, 0, availableSampleCount);
                localEndExclusive = Math.Clamp(localEndExclusive, 0, availableSampleCount);
            }

            if (localEndExclusive <= localStartIndex + 1)
            {
                continue;
            }

            ranges.Add(new SampleRange(
                segment.FirstDenseIndex + localStartIndex,
                segment.FirstDenseIndex + localEndExclusive));
        }

        return ranges;
    }

    private static bool TryGetLegacySampleRange(
        int pairedCount,
        int sampleRate,
        TelemetryTimeRange? analysisRange,
        out int startIndex,
        out int endExclusive)
    {
        startIndex = 0;
        endExclusive = pairedCount;

        if (analysisRange is null)
        {
            return true;
        }

        if (sampleRate <= 0)
        {
            return false;
        }

        startIndex = (int)Math.Floor(analysisRange.Value.StartSeconds * sampleRate);
        endExclusive = (int)Math.Ceiling(analysisRange.Value.EndSeconds * sampleRate);
        startIndex = Math.Clamp(startIndex, 0, pairedCount);
        endExclusive = Math.Clamp(endExclusive, 0, pairedCount);
        return endExclusive > startIndex + 1;
    }

    private static List<StrokeBuildPlan> BuildStrokePlans(
        IReadOnlyList<Stroke> strokes,
        IReadOnlyList<double> travel,
        IReadOnlyList<double> velocity,
        int pairedCount,
        IReadOnlyList<SampleRange> sampleRanges)
    {
        var plans = new List<StrokeBuildPlan>();
        foreach (var stroke in strokes)
        {
            foreach (var sampleRange in sampleRanges)
            {
                var strokeStart = Math.Max(stroke.Start, sampleRange.Start);
                var strokeEndInclusive = Math.Min(stroke.End, sampleRange.EndExclusive - 1);
                if (strokeEndInclusive <= strokeStart)
                {
                    continue;
                }

                strokeStart = Math.Clamp(strokeStart, 0, pairedCount - 1);
                strokeEndInclusive = Math.Clamp(strokeEndInclusive, 0, pairedCount - 1);
                if (strokeEndInclusive <= strokeStart)
                {
                    continue;
                }

                var validPointCount = 0;
                for (var index = strokeStart; index <= strokeEndInclusive; index++)
                {
                    if (double.IsFinite(travel[index]) && double.IsFinite(velocity[index]))
                    {
                        validPointCount++;
                    }
                }

                if (validPointCount >= 2)
                {
                    plans.Add(new StrokeBuildPlan(strokeStart, strokeEndInclusive, validPointCount));
                }
            }
        }

        return plans;
    }

    private static List<StrokeBuildPlan> CapStrokeCount(
        IReadOnlyList<StrokeBuildPlan> plans)
    {
        if (plans.Count <= MaximumStrokeTracesPerSide)
        {
            return [.. plans];
        }

        var stride = (int)Math.Ceiling(plans.Count / (double)MaximumStrokeTracesPerSide);
        var retained = new List<StrokeBuildPlan>();
        for (var index = 0; index < plans.Count; index++)
        {
            if (index % stride == 0 || index == plans.Count - 1)
            {
                retained.Add(plans[index]);
            }
        }

        return retained;
    }

    private static int CalculatePointStride(IReadOnlyList<StrokeBuildPlan> plans)
    {
        var totalPointCount = plans.Sum(plan => plan.ValidPointCount);
        return totalPointCount <= MaximumPointsPerSide
            ? 1
            : (int)Math.Ceiling(totalPointCount / (double)MaximumPointsPerSide);
    }

    private static PositionVelocityStrokeTrace BuildStrokeTrace(
        StrokeBuildPlan plan,
        IReadOnlyList<double> travel,
        IReadOnlyList<double> velocity,
        double maxTravel,
        int stride)
    {
        var lastValidPointIndex = plan.ValidPointCount - 1;
        var retainedPointCount = lastValidPointIndex / stride + 1;
        if (lastValidPointIndex % stride != 0)
        {
            retainedPointCount++;
        }

        var positions = new double[retainedPointCount];
        var velocities = new double[retainedPointCount];
        var validPointIndex = 0;
        var retainedPointIndex = 0;
        for (var index = plan.Start; index <= plan.EndInclusive; index++)
        {
            var sampleTravel = travel[index];
            var sampleVelocity = velocity[index];
            if (!double.IsFinite(sampleTravel) || !double.IsFinite(sampleVelocity))
            {
                continue;
            }

            if (validPointIndex % stride == 0 || validPointIndex == lastValidPointIndex)
            {
                positions[retainedPointIndex] = Math.Clamp(sampleTravel / maxTravel * 100.0, 0.0, 100.0);
                velocities[retainedPointIndex] = sampleVelocity;
                retainedPointIndex++;
            }

            validPointIndex++;
        }

        return new PositionVelocityStrokeTrace(positions, velocities);
    }

    private static double[,] BuildHeatmap(
        IReadOnlyList<PositionVelocityStrokeTrace> strokes,
        double velocityLimit)
    {
        var values = new double[HeatmapVelocityBinCount, HeatmapPositionBinCount];
        var pointCount = 0;
        foreach (var stroke in strokes)
        {
            for (var index = 0; index < stroke.Count; index++)
            {
                var positionBin = Math.Clamp(
                    (int)Math.Floor(stroke.PositionPercent[index] / 100.0 * HeatmapPositionBinCount),
                    0,
                    HeatmapPositionBinCount - 1);
                var velocityBin = Math.Clamp(
                    (int)Math.Floor(
                        (stroke.VelocityMmPerSecond[index] + velocityLimit) /
                        (velocityLimit * 2.0) *
                        HeatmapVelocityBinCount),
                    0,
                    HeatmapVelocityBinCount - 1);
                values[velocityBin, positionBin]++;
                pointCount++;
            }
        }

        for (var velocityBin = 0; velocityBin < HeatmapVelocityBinCount; velocityBin++)
        {
            for (var positionBin = 0; positionBin < HeatmapPositionBinCount; positionBin++)
            {
                values[velocityBin, positionBin] = values[velocityBin, positionBin] > 0
                    ? values[velocityBin, positionBin] / pointCount * 100.0
                    : double.NaN;
            }
        }

        return values;
    }

    private static double CalculateVelocityLimit(
        IReadOnlyList<PositionVelocityStrokeTrace>? front,
        IReadOnlyList<PositionVelocityStrokeTrace>? rear)
    {
        var maxAbsVelocity = EnumerateVelocities(front)
            .Concat(EnumerateVelocities(rear))
            .Select(Math.Abs)
            .DefaultIfEmpty(0)
            .Max();

        if (maxAbsVelocity <= 0 || !double.IsFinite(maxAbsVelocity))
        {
            return MinimumVelocityLimitMmPerSecond;
        }

        return Math.Max(
            MinimumVelocityLimitMmPerSecond,
            Math.Ceiling(maxAbsVelocity / VelocityLimitRoundingMmPerSecond) *
            VelocityLimitRoundingMmPerSecond);
    }

    private static IEnumerable<double> EnumerateVelocities(IReadOnlyList<PositionVelocityStrokeTrace>? strokes)
    {
        return strokes?.SelectMany(stroke => stroke.VelocityMmPerSecond) ?? [];
    }

    private readonly record struct SampleRange(
        int Start,
        int EndExclusive);

    private readonly record struct StrokeBuildPlan(
        int Start,
        int EndInclusive,
        int ValidPointCount);
}
