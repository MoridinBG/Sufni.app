using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class StrokeCoarseIndexesTests
{
    [Fact]
    public void AffectedStatistics_WithValidLegacyPair_ReuseBothCoarseArraysAndIgnoreFineIndexes()
    {
        var telemetry = CreateTelemetryData(
            digitizedTravel: [2, 2, 2, 2],
            digitizedVelocity: [0, 0, 0, 0],
            fineDigitizedVelocity: [99]);

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
            digitizedVelocity: [0],
            fineDigitizedVelocity: [99, 99, 99, 99]);

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
    public async Task AffectedStatistics_WithConcurrentRepeatedAccess_ReuseOneDerivedResult()
    {
        var telemetry = CreateTelemetryData();

        var concurrent = await Task.WhenAll(
            Enumerable.Range(0, 16)
                .Select(_ => Task.Run(
                    () => TelemetryStatistics.CalculateTravelHistogram(
                        telemetry,
                        SuspensionType.Front),
                    TestContext.Current.CancellationToken)));
        telemetry.Front.Travel = [19, 19, 19, 19];
        telemetry.Front.Velocity = [500, 500, 500, 500];
        var repeated = TelemetryStatistics.CalculateTravelHistogram(
            telemetry,
            SuspensionType.Front);

        Assert.All(concurrent, histogram =>
            Assert.Equal(concurrent[0].Values, histogram.Values));
        Assert.Equal(concurrent[0].Values, repeated.Values);
    }

    [Fact]
    public void LegacyAndDerivedIndexes_ProduceIdenticalAffectedStatistics()
    {
        var legacy = CreateTelemetryData(
            digitizedTravel: [1, 4, 11, 18],
            digitizedVelocity: [1, 1, 2, 2],
            fineDigitizedVelocity: [9, 9, 9, 9]);
        var derived = CreateTelemetryData();
        var velocityOptions = new VelocityStatisticsOptions(
            VelocityAverageMode: VelocityAverageMode.SampleAveraged);
        var selection = new DampingRangeSelection(
            SuspensionType.Front,
            VelocityAverageMode.SampleAveraged,
            VelocityBinIndex: 1,
            TravelBinStartIndex: 0,
            TravelBinEndIndex: 2);

        var legacyTravel = TelemetryStatistics.CalculateTravelHistogram(
            legacy,
            SuspensionType.Front);
        var derivedTravel = TelemetryStatistics.CalculateTravelHistogram(
            derived,
            SuspensionType.Front);
        var legacyVelocity = TelemetryStatistics.CalculateVelocityHistogram(
            legacy,
            SuspensionType.Front,
            velocityOptions);
        var derivedVelocity = TelemetryStatistics.CalculateVelocityHistogram(
            derived,
            SuspensionType.Front,
            velocityOptions);
        var legacyHighlights = TelemetryStatistics.CalculateHighlightRanges(
            legacy,
            selection);
        var derivedHighlights = TelemetryStatistics.CalculateHighlightRanges(
            derived,
            selection);

        Assert.Equal(legacyTravel.Bins, derivedTravel.Bins);
        Assert.Equal(legacyTravel.Values, derivedTravel.Values);
        Assert.Equal(legacyVelocity.Bins, derivedVelocity.Bins);
        Assert.Equal(
            legacyVelocity.Values.SelectMany(values => values),
            derivedVelocity.Values.SelectMany(values => values));
        Assert.Equal(legacyHighlights, derivedHighlights);
    }

    private static TelemetryData CreateTelemetryData(
        int[]? digitizedTravel = null,
        int[]? digitizedVelocity = null,
        int[]? fineDigitizedVelocity = null)
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
            DigitizedTravel = digitizedTravel ?? [],
            DigitizedVelocity = digitizedVelocity ?? [],
            FineDigitizedVelocity = fineDigitizedVelocity ?? [],
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
                Travel = travel,
                Velocity = velocity,
                TravelBins = Enumerable.Range(0, 21).Select(index => (double)index).ToArray(),
                VelocityBins = velocityBins,
                FineVelocityBins = [],
                Segments = segments,
                HasGaps = segments.Length > 1,
                Strokes = Strokes.FromCategorized([stroke], [], []),
            },
            Rear = new Suspension
            {
                Present = false,
                Travel = [],
                Velocity = [],
                TravelBins = [],
                VelocityBins = [],
                FineVelocityBins = [],
                Strokes = Strokes.FromCategorized([], [], []),
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
