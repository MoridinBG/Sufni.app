using System.Collections.Immutable;
using System.Diagnostics;

namespace Sufni.Telemetry;

public static partial class TelemetryStatistics
{
    public static bool HasVibrationData(TelemetryData telemetryData, ImuLocation location)
    {
        return telemetryData.ImuData is { ActiveLocations.Length: > 0 } &&
            telemetryData.ImuData.HasSamples &&
            telemetryData.ImuData.ActiveLocations.Contains((byte)location);
    }

    public static VibrationStats? CalculateVibration(
        TelemetryData telemetryData,
        ImuLocation location,
        SuspensionType pairedSuspension,
        TelemetryTimeSelection? range = null)
    {
        if (!HasVibrationData(telemetryData, location))
        {
            return null;
        }

        Debug.Assert(telemetryData.ImuData is not null);
        var suspension = GetSuspension(telemetryData, pairedSuspension);
        var imu = telemetryData.ImuData;
        var rate = telemetryData.Metadata.SampleRate;
        if (!suspension.Present || suspension.MaxTravel is not > 0 || suspension.Travel.Length < 2 ||
            rate <= 0 || imu.SampleRate <= 0 || suspension.Strokes is not { } strokes ||
            strokes.Compressions.IsDefaultOrEmpty && strokes.Rebounds.IsDefaultOrEmpty)
            return null;

        var meta = imu.Meta.FirstOrDefault(entry => entry.LocationId == (byte)location);
        if (meta is null || meta.AccelLsbPerG <= 0) return null;
        var totalMovement = 0d;
        foreach (var interval in SuspensionSampleInterval.Select(suspension.Segments, suspension.Travel.Length, rate, range))
            for (var i = interval.Start + 1; i < interval.EndExclusive; i++)
                totalMovement += Math.Abs(suspension.Travel[i] - suspension.Travel[i - 1]);

        return CalculateVibrationFromSegments(telemetryData, location, suspension, meta, range,
            strokes.Compressions, strokes.Rebounds, suspension.HasGaps || imu.HasGaps, totalMovement);
    }

    private static VibrationStats? CalculateVibrationFromSegments(
        TelemetryData telemetryData,
        ImuLocation location,
        Suspension suspension,
        ImuMetaEntry meta,
        TelemetryTimeSelection? selection,
        ImmutableArray<Stroke> compressions,
        ImmutableArray<Stroke> rebounds,
        bool segmented,
        double totalMovement)
    {
        Debug.Assert(telemetryData.ImuData is not null);
        Debug.Assert(suspension.MaxTravel is > 0);
        var imuData = telemetryData.ImuData;
        var imuSegments = new OrderedImuSegments(imuData.SampleSegments, (byte)location);
        var cursor = new VibrationTraversalCursor(
            suspension,
            telemetryData.Metadata.SampleRate,
            compressions,
            rebounds,
            segmented);
        var compression = new VibrationAccumulator();
        var rebound = new VibrationAccumulator();
        var other = new VibrationAccumulator();
        var overall = new VibrationAccumulator();

        for (var segmentIndex = 0; segmentIndex < imuSegments.Count; segmentIndex++)
        {
            var segment = imuSegments[segmentIndex];
            if (segment.LocationId != (byte)location)
            {
                continue;
            }

            var startSeconds = segment.FirstMonotonicDeltaUs / 1_000_000.0;
            for (var sampleIndex = 0; sampleIndex < segment.Count; sampleIndex++)
            {
                var sampleTime = startSeconds + sampleIndex / (double)imuData.SampleRate;
                if (selection is not null && !selection.Contains(sampleTime))
                {
                    continue;
                }

                if (!cursor.TrySample(sampleTime, out var travel, out var kind))
                {
                    continue;
                }

                var record = segment[sampleIndex];
                var positionRatio = travel / suspension.MaxTravel.Value;
                var g = Math.Abs(record.Az / (double)meta.AccelLsbPerG - 1.0);
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
        }

        if (overall.SumG <= 0)
        {
            return null;
        }

        var totalGSeconds = overall.SumG / imuData.SampleRate;
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

    private readonly struct OrderedImuSegments
    {
        private readonly ImuSampleSegmentCollection source;
        private readonly ImuSampleSegment[]? sorted;

        public OrderedImuSegments(ImuSampleSegmentCollection source, byte locationId)
        {
            this.source = source;
            sorted = null;
            var matchingCount = 0;
            var ordered = true;
            var hasPrevious = false;
            ulong previousStart = 0;
            for (var index = 0; index < source.Count; index++)
            {
                var segment = source[index];
                if (segment.LocationId != locationId)
                {
                    continue;
                }

                matchingCount++;
                if (hasPrevious && segment.FirstMonotonicDeltaUs < previousStart)
                {
                    ordered = false;
                }

                previousStart = segment.FirstMonotonicDeltaUs;
                hasPrevious = true;
            }

            if (ordered)
            {
                return;
            }

            sorted = new ImuSampleSegment[matchingCount];
            var sortedIndex = 0;
            for (var index = 0; index < source.Count; index++)
            {
                var segment = source[index];
                if (segment.LocationId == locationId)
                {
                    sorted[sortedIndex++] = segment;
                }
            }

            StableSort(sorted);
        }

        public int Count => sorted?.Length ?? source.Count;

        public ImuSampleSegment this[int index] => sorted is null ? source[index] : sorted[index];

        private static void StableSort(ImuSampleSegment[] segments)
        {
            for (var index = 1; index < segments.Length; index++)
            {
                var current = segments[index];
                var insertionIndex = index;
                while (insertionIndex > 0 &&
                       segments[insertionIndex - 1].FirstMonotonicDeltaUs > current.FirstMonotonicDeltaUs)
                {
                    segments[insertionIndex] = segments[insertionIndex - 1];
                    insertionIndex--;
                }

                segments[insertionIndex] = current;
            }
        }
    }

    private struct VibrationTraversalCursor
    {
        private readonly ImmutableArray<double> travel;
        private readonly int sampleRate;
        private readonly bool segmented;
        private SuspensionSegmentCursor suspensionCursor;
        private StrokeCursor compressionCursor;
        private StrokeCursor reboundCursor;
        private bool hasPreviousSeconds;
        private double previousSeconds;

        public VibrationTraversalCursor(
            Suspension suspension,
            int sampleRate,
            ImmutableArray<Stroke> compressions,
            ImmutableArray<Stroke> rebounds,
            bool segmented)
        {
            travel = suspension.Travel;
            this.sampleRate = sampleRate;
            this.segmented = segmented;
            suspensionCursor = new SuspensionSegmentCursor(suspension.Segments, suspension.Travel, sampleRate);
            compressionCursor = new StrokeCursor(
                compressions,
                sampleRate,
                useTime: segmented,
                maximumDenseIndex: suspension.Travel.Length - 1);
            reboundCursor = new StrokeCursor(
                rebounds,
                sampleRate,
                useTime: segmented,
                maximumDenseIndex: suspension.Travel.Length - 1);
            hasPreviousSeconds = false;
            previousSeconds = 0;
        }

        public bool TrySample(double seconds, out double sampledTravel, out StrokeKind kind)
        {
            if (hasPreviousSeconds && seconds < previousSeconds)
            {
                suspensionCursor.Reset();
                compressionCursor.Reset();
                reboundCursor.Reset();
            }

            hasPreviousSeconds = true;
            previousSeconds = seconds;
            double strokePosition;
            if (segmented)
            {
                if (!suspensionCursor.TrySample(seconds, out sampledTravel))
                {
                    kind = StrokeKind.Other;
                    return false;
                }

                strokePosition = seconds;
            }
            else
            {
                var suspensionIndex = Math.Clamp(
                    (int)(seconds * sampleRate),
                    0,
                    travel.Length - 1);
                sampledTravel = travel[suspensionIndex];
                strokePosition = suspensionIndex;
            }

            var compression = compressionCursor.Contains(strokePosition);
            var rebound = reboundCursor.Contains(strokePosition);
            kind = segmented
                ? compression ? StrokeKind.Compression : rebound ? StrokeKind.Rebound : StrokeKind.Other
                : rebound ? StrokeKind.Rebound : compression ? StrokeKind.Compression : StrokeKind.Other;
            return true;
        }
    }

    private struct SuspensionSegmentCursor
    {
        private readonly IReadOnlyList<ProcessedSuspensionSegment> segments;
        private readonly IReadOnlyList<double> values;
        private readonly int sampleRate;
        private int segmentIndex;

        public SuspensionSegmentCursor(IReadOnlyList<ProcessedSuspensionSegment> segments, IReadOnlyList<double> values, int sampleRate)
        {
            this.segments = OrderedSuspensionSegments(segments);
            this.values = values;
            this.sampleRate = sampleRate;
            segmentIndex = 0;
        }

        public void Reset() => segmentIndex = 0;

        public bool TrySample(double seconds, out double value)
        {
            value = 0;
            while (segmentIndex < segments.Count)
            {
                var segment = segments[segmentIndex];
                if (segment.SampleCount <= 0)
                {
                    segmentIndex++;
                    continue;
                }

                var segmentEndSeconds = segment.StartSeconds + (segment.SampleCount - 1) / (double)sampleRate;
                if (seconds < segment.StartSeconds)
                {
                    return false;
                }

                if (seconds > segmentEndSeconds)
                {
                    segmentIndex++;
                    continue;
                }

                var position = (seconds - segment.StartSeconds) * sampleRate;
                var lower = (int)Math.Floor(position);
                var upper = Math.Min(lower + 1, segment.SampleCount - 1);
                if (lower < 0 || lower >= segment.SampleCount)
                {
                    return false;
                }

                var lowerDenseIndex = segment.FirstDenseIndex + lower;
                var upperDenseIndex = segment.FirstDenseIndex + upper;
                if (lowerDenseIndex < 0 ||
                    lowerDenseIndex >= values.Count ||
                    upperDenseIndex < 0 ||
                    upperDenseIndex >= values.Count)
                {
                    return false;
                }

                var fraction = position - lower;
                value = values[lowerDenseIndex] + (values[upperDenseIndex] - values[lowerDenseIndex]) * fraction;
                return true;
            }

            return false;
        }

        private static IReadOnlyList<ProcessedSuspensionSegment> OrderedSuspensionSegments(IReadOnlyList<ProcessedSuspensionSegment> source)
        {
            for (var index = 1; index < source.Count; index++)
            {
                if (source[index - 1].StartSeconds.CompareTo(source[index].StartSeconds) <= 0)
                {
                    continue;
                }

                var sorted = source.ToArray();
                StableSort(sorted);
                return sorted;
            }

            return source;
        }

        private static void StableSort(ProcessedSuspensionSegment[] segments)
        {
            for (var index = 1; index < segments.Length; index++)
            {
                var current = segments[index];
                var insertionIndex = index;
                while (insertionIndex > 0 &&
                       segments[insertionIndex - 1].StartSeconds.CompareTo(current.StartSeconds) > 0)
                {
                    segments[insertionIndex] = segments[insertionIndex - 1];
                    insertionIndex--;
                }

                segments[insertionIndex] = current;
            }
        }
    }

    private struct StrokeCursor
    {
        private readonly IReadOnlyList<Stroke> strokes;
        private readonly int sampleRate;
        private readonly bool useTime;
        private readonly int maximumDenseIndex;
        private int nextIndex;
        private double maximumEnd;

        public StrokeCursor(
            IReadOnlyList<Stroke> strokes,
            int sampleRate,
            bool useTime,
            int maximumDenseIndex)
        {
            this.sampleRate = sampleRate;
            this.useTime = useTime;
            this.maximumDenseIndex = maximumDenseIndex;
            this.strokes = OrderedStrokes(strokes, sampleRate, useTime, maximumDenseIndex);
            nextIndex = 0;
            maximumEnd = double.NegativeInfinity;
        }

        public void Reset()
        {
            nextIndex = 0;
            maximumEnd = double.NegativeInfinity;
        }

        public bool Contains(double position)
        {
            while (nextIndex < strokes.Count && Start(strokes[nextIndex]) <= position)
            {
                maximumEnd = Math.Max(maximumEnd, End(strokes[nextIndex]));
                nextIndex++;
            }

            return position <= maximumEnd;
        }

        private double Start(Stroke stroke) => useTime
            ? StrokeStartSeconds(stroke, sampleRate)
            : Math.Clamp(stroke.Start, 0, maximumDenseIndex);

        private double End(Stroke stroke) => useTime
            ? StrokeEndSeconds(stroke, sampleRate)
            : Math.Clamp(stroke.End, 0, maximumDenseIndex);

        private static IReadOnlyList<Stroke> OrderedStrokes(
            IReadOnlyList<Stroke> source,
            int sampleRate,
            bool useTime,
            int maximumDenseIndex)
        {
            static double Start(Stroke stroke, int sampleRate, bool useTime, int maximumDenseIndex) => useTime
                ? StrokeStartSeconds(stroke, sampleRate)
                : Math.Clamp(stroke.Start, 0, maximumDenseIndex);

            for (var index = 1; index < source.Count; index++)
            {
                var previousStart = Start(source[index - 1], sampleRate, useTime, maximumDenseIndex);
                var currentStart = Start(source[index], sampleRate, useTime, maximumDenseIndex);
                if (previousStart.CompareTo(currentStart) <= 0)
                {
                    continue;
                }

                var sorted = source.ToArray();
                for (var sortIndex = 1; sortIndex < sorted.Length; sortIndex++)
                {
                    var current = sorted[sortIndex];
                    var insertionIndex = sortIndex;
                    var insertionStart = Start(current, sampleRate, useTime, maximumDenseIndex);
                    while (insertionIndex > 0 &&
                           Start(sorted[insertionIndex - 1], sampleRate, useTime, maximumDenseIndex)
                               .CompareTo(insertionStart) > 0)
                    {
                        sorted[insertionIndex] = sorted[insertionIndex - 1];
                        insertionIndex--;
                    }

                    sorted[insertionIndex] = current;
                }

                return sorted;
            }

            return source;
        }
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
