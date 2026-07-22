namespace Sufni.Telemetry;

internal readonly record struct StrokeCoarseIndexPair(
    ReadOnlyMemory<int> Travel,
    ReadOnlyMemory<int> Velocity);

internal sealed class StrokeCoarseIndexes
{
    private readonly Suspension frontSuspension;
    private readonly Suspension rearSuspension;
    private readonly Lazy<SideIndexes> front;
    private readonly Lazy<SideIndexes> rear;

    internal StrokeCoarseIndexes(TelemetryData telemetryData)
    {
        frontSuspension = telemetryData.Front;
        rearSuspension = telemetryData.Rear;
        front = CreateSide(frontSuspension);
        rear = CreateSide(rearSuspension);
    }

    internal StrokeCoarseIndexPair Get(Suspension suspension, Stroke stroke)
    {
        if (ReferenceEquals(suspension, frontSuspension))
        {
            return front.Value.Get(stroke);
        }

        if (ReferenceEquals(suspension, rearSuspension))
        {
            return rear.Value.Get(stroke);
        }

        throw new ArgumentException("Suspension does not belong to the cached telemetry result.", nameof(suspension));
    }

    private static Lazy<SideIndexes> CreateSide(Suspension suspension) =>
        new(
            () => SideIndexes.Create(suspension),
            LazyThreadSafetyMode.ExecutionAndPublication);

    private sealed class SideIndexes(Dictionary<Stroke, StrokeCoarseIndexPair> indexes)
    {
        internal StrokeCoarseIndexPair Get(Stroke stroke) =>
            indexes.TryGetValue(stroke, out var result)
                ? result
                : throw new ArgumentException("Stroke does not belong to the cached suspension.", nameof(stroke));

        internal static SideIndexes Create(Suspension suspension)
        {
            var indexes = new Dictionary<Stroke, StrokeCoarseIndexPair>();
            var segmentVelocityBins = new Dictionary<ProcessedSuspensionSegment, double[]>();
            foreach (var stroke in suspension.Strokes.Compressions.Concat(suspension.Strokes.Rebounds))
            {
                var count = checked(stroke.End - stroke.Start + 1);
                var velocityBins = VelocityBinsForStroke(
                    suspension,
                    stroke,
                    segmentVelocityBins);
                indexes.Add(
                    stroke,
                    IsValidLegacyPair(stroke, count, suspension.TravelBins.Length - 1, velocityBins.Length - 1)
                        ? new StrokeCoarseIndexPair(stroke.DigitizedTravel, stroke.DigitizedVelocity)
                        : Derive(suspension, stroke, count, velocityBins));
            }

            return new SideIndexes(indexes);
        }

        private static StrokeCoarseIndexPair Derive(
            Suspension suspension,
            Stroke stroke,
            int count,
            double[] velocityBins)
        {
            if (stroke.Start < 0 || count <= 0 || stroke.End >= suspension.Travel.Length || stroke.End >= suspension.Velocity.Length)
            {
                throw new InvalidDataException("Stroke range is outside the processed suspension samples.");
            }

            var travel = HistogramBuilder.Digitize(
                suspension.Travel.AsSpan(stroke.Start, count),
                suspension.TravelBins);
            var velocity = HistogramBuilder.Digitize(
                suspension.Velocity.AsSpan(stroke.Start, count),
                velocityBins);
            return new StrokeCoarseIndexPair(travel, velocity);
        }

        private static double[] VelocityBinsForStroke(
            Suspension suspension,
            Stroke stroke,
            Dictionary<ProcessedSuspensionSegment, double[]> segmentVelocityBins)
        {
            if (suspension.Segments.Length <= 1)
            {
                return suspension.VelocityBins;
            }

            foreach (var segment in suspension.Segments)
            {
                var segmentEnd = checked(segment.FirstDenseIndex + segment.SampleCount);
                if (stroke.Start < segment.FirstDenseIndex || stroke.End >= segmentEnd)
                {
                    continue;
                }

                if (!segmentVelocityBins.TryGetValue(segment, out var bins))
                {
                    bins = HistogramBuilder.CreateVelocityBins(
                        suspension.Velocity.AsSpan(segment.FirstDenseIndex, segment.SampleCount),
                        Parameters.VelocityHistStep);
                    segmentVelocityBins.Add(segment, bins);
                }

                return bins;
            }

            throw new InvalidDataException("Stroke range does not belong to one processed suspension segment.");
        }

        private static bool IsValidLegacyPair(
            Stroke stroke,
            int expectedCount,
            int travelBinCount,
            int velocityBinCount)
        {
            return expectedCount > 0 &&
                stroke.Stat.Count == expectedCount &&
                stroke.DigitizedTravel is { } travel &&
                stroke.DigitizedVelocity is { } velocity &&
                travel.Length == expectedCount &&
                velocity.Length == expectedCount &&
                travel.All(index => index >= 0 && index < travelBinCount) &&
                velocity.All(index => index >= 0 && index < velocityBinCount);
        }
    }
}
