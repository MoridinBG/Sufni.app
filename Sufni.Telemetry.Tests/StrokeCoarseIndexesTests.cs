using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class StrokeCoarseIndexesTests
{
    [Fact]
    public void AffectedStatistics_WithValidLegacyPair_ReuseBothCoarseArrays()
    {
        var telemetry = CreateTelemetryData(
            digitizedTravel: [2, 2, 2, 2],
            digitizedVelocity: [0, 0, 0, 0]);

        var travel = TelemetryStatistics.CalculateTravelHistogram(
            telemetry,
            SuspensionType.Front);
        var velocity = TelemetryStatistics.CalculateVelocityHistogram(
            telemetry,
            SuspensionType.Front);

        Assert.Equal(100, travel.Values[2]);
        Assert.Equal(100, velocity.Values[0][1]);
        Assert.Equal(
            100,
            velocity.Values.SelectMany(values => values).Sum(),
            precision: 6);
    }

    [Fact]
    public void AffectedStatistics_WithInvalidLegacyPair_DeriveBothCoarseArrays()
    {
        var telemetry = CreateTelemetryData(
            digitizedTravel: [2, 2, 2, 2],
            digitizedVelocity: [0]);

        var travel = TelemetryStatistics.CalculateTravelHistogram(
            telemetry,
            SuspensionType.Front);
        var velocity = TelemetryStatistics.CalculateVelocityHistogram(
            telemetry,
            SuspensionType.Front);

        Assert.Equal(0, travel.Values[2]);
        Assert.Equal(25, travel.Values[1]);
        Assert.Equal(25, travel.Values[4]);
        Assert.Equal(25, travel.Values[11]);
        Assert.Equal(25, travel.Values[18]);
        Assert.Equal(25, velocity.Values[1][0]);
        Assert.Equal(25, velocity.Values[1][2]);
        Assert.Equal(25, velocity.Values[2][5]);
        Assert.Equal(25, velocity.Values[2][9]);
    }

    [Fact]
    public void AffectedStatistics_WithMultipleSegments_UseStrokeSegmentVelocityDomain()
    {
        var stroke = new Stroke
        {
            Start = 2,
            End = 3,
            Stat = new StrokeStat { Count = 2 },
        };
        var telemetry = CreateTelemetryData(
            stroke,
            travel: [1, 2, 11, 18],
            velocity: [-50, 50, 950, 1050],
            velocityBins: CreateVelocityBins([-50, 50, 950, 1050]),
            segments:
            [
                new ProcessedSuspensionSegment
                {
                    FirstDenseIndex = 0,
                    SampleCount = 2,
                },
                new ProcessedSuspensionSegment
                {
                    FirstDenseIndex = 2,
                    SampleCount = 2,
                },
            ]);

        var histogram = TelemetryStatistics.CalculateVelocityHistogram(
            telemetry,
            SuspensionType.Front);

        Assert.Equal(50, histogram.Values[1][5]);
        Assert.Equal(50, histogram.Values[2][9]);
        Assert.Equal(0, histogram.Values[11][5]);
        Assert.Equal(0, histogram.Values[12][9]);
    }

    [Fact]
    public void ReplacedTelemetry_OwnsFreshLazyIndexesWithoutChangingTheOriginal()
    {
        var original = CreateTelemetryData();
        var before = TelemetryStatistics.CalculateTravelHistogram(original, SuspensionType.Front);
        var replacement = original with { Front = original.Front with { Travel = [19, 19, 19, 19] } };

        var changed = TelemetryStatistics.CalculateTravelHistogram(replacement, SuspensionType.Front);

        Assert.False(before.Values.SequenceEqual(changed.Values));
        Assert.Equal(before.Values, TelemetryStatistics.CalculateTravelHistogram(original, SuspensionType.Front).Values);
    }

    private static TelemetryData CreateTelemetryData(
        int[]? digitizedTravel = null,
        int[]? digitizedVelocity = null)
    {
        var stroke = new Stroke
        {
            Start = 0,
            End = 3,
            Stat = new StrokeStat
            {
                Count = 4,
                MaxTravel = 18,
                MaxVelocity = 50,
            },
            DigitizedTravel = [.. (digitizedTravel ?? [])],
            DigitizedVelocity = [.. (digitizedVelocity ?? [])],
        };
        return CreateTelemetryData(
            stroke,
            travel: [1, 4, 11, 18],
            velocity: [-50, -50, 50, 50],
            velocityBins: [-150, -50, 50, 150],
            segments: []);
    }

    private static TelemetryData CreateTelemetryData(
        Stroke stroke,
        double[] travel,
        double[] velocity,
        double[] velocityBins,
        ProcessedSuspensionSegment[] segments)
    {
        return new TelemetryData
        {
            Metadata = new Metadata
            {
                SourceName = "test",
                Version = 5,
                SampleRate = 10,
                Duration = travel.Length / 10.0,
            },
            Front = new Suspension
            {
                Present = true,
                MaxTravel = 20,
                Travel = [.. (travel)],
                Velocity = [.. (velocity)],
                TravelBins = [.. (Enumerable.Range(0, 21).Select(index => (double)index).ToArray())],
                VelocityBins = [.. (velocityBins)],
                Segments = [.. (segments)],
                HasGaps = segments.Length > 1,
                Strokes = new Strokes { Compressions = [stroke], Rebounds = [] },
            },
            Rear = new Suspension
            {
                Present = false,
                Travel = [],
                Velocity = [],
                TravelBins = [],
                VelocityBins = [],
                Strokes = new Strokes { Compressions = [], Rebounds = [] },
            },
            Airtimes = [],
        };
    }

    private static double[] CreateVelocityBins(double[] velocity)
    {
        var minimum = velocity.Min();
        var maximum = velocity.Max();
        var step = Parameters.VelocityHistStep;
        var min = (Math.Floor(minimum / step) - 0.5) * step;
        var max = (Math.Floor(maximum / step) + 1.5) * step;
        return HistogramBuilder.Linspace(
            min,
            max,
            (int)((max - min) / step) + 1);
    }
}
