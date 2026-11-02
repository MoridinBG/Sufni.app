namespace Sufni.Telemetry.Tests;

public class PositionVelocityTraceBuilderTests
{
    [Fact]
    public void Build_FullSessionDataBuildsFrontAndRearSideTraces()
    {
        var frontCompression = new Stroke { Start = 0, End = 2 };
        var frontRebound = new Stroke { Start = 2, End = 4 };
        var rearCompression = new Stroke { Start = 1, End = 3 };
        var telemetry = CreateTelemetry(
            front: CreateSuspension(
                maxTravel: 100,
                travel: [0, 50, 100, 50, 0],
                velocity: [0, 700, 1500, -800, -200],
                compressions: [frontCompression],
                rebounds: [frontRebound]),
            rear: CreateSuspension(
                maxTravel: 80,
                travel: [0, 20, 40, 80, 40],
                velocity: [0, 500, 900, 1200, -300],
                compressions: [rearCompression],
                rebounds: []));

        var traceSet = PositionVelocityTraceBuilder.Build(telemetry, analysisRange: null);

        var front = traceSet.Front;
        var rear = traceSet.Rear;
        Assert.NotNull(front);
        Assert.NotNull(rear);
        Assert.Equal(2, front.Strokes.Count);
        Assert.Single(rear.Strokes);
        Assert.Equal([0, 50, 100], front.Strokes[0].PositionPercent);
        Assert.Equal([0, 700, 1500], front.Strokes[0].VelocityMmPerSecond);
        Assert.Equal([100, 50, 0], front.Strokes[1].PositionPercent);
        Assert.Equal([1500, -800, -200], front.Strokes[1].VelocityMmPerSecond);
        Assert.Equal(1500, traceSet.VelocityLimitMmPerSecond);
    }

    [Fact]
    public void Build_UsesPersistedCompressionAndReboundStrokesWithoutReconstructingIdlingStrokes()
    {
        var telemetry = CreateTelemetry(
            front: CreateSuspension(
                maxTravel: 100,
                travel: [0, 50, 100, 50, 0],
                velocity: [0, 700, -700, 700, -700],
                compressions: [],
                rebounds: []),
            rear: CreateMissingSuspension());

        var traceSet = PositionVelocityTraceBuilder.Build(telemetry, analysisRange: null);

        Assert.Null(traceSet.Front);
        Assert.Equal(1000, traceSet.VelocityLimitMmPerSecond);
    }

    [Fact]
    public void Build_ClampsPositionPercentAndKeepsVelocityValuesInMillimetersPerSecond()
    {
        var telemetry = CreateTelemetry(
            front: CreateSuspension(
                maxTravel: 100,
                travel: [-10, 50, 140],
                velocity: [-1234.5, 0, 2345.5],
                compressions: [new Stroke { Start = 0, End = 2 }],
                rebounds: []),
            rear: CreateMissingSuspension());

        var front = PositionVelocityTraceBuilder.Build(telemetry, null).Front;
        Assert.NotNull(front);
        var trace = Assert.Single(front.Strokes);

        Assert.Equal([0, 50, 100], trace.PositionPercent);
        Assert.Equal([-1234.5, 0, 2345.5], trace.VelocityMmPerSecond);
    }

    [Fact]
    public void Build_AnalysisRangeClipsPartialStrokesAndSkipsOutsideStrokes()
    {
        var telemetry = CreateTelemetry(
            sampleRate: 10,
            front: CreateSuspension(
                maxTravel: 100,
                travel: [0, 10, 20, 30, 40, 50, 60, 70],
                velocity: [0, 100, 200, 300, 400, 500, 600, 700],
                compressions:
                [
                    new Stroke { Start = 0, End = 1 },
                    new Stroke { Start = 0, End = 7 },
                    new Stroke { Start = 6, End = 7 }
                ],
                rebounds: []),
            rear: CreateMissingSuspension());

        var traceSet = PositionVelocityTraceBuilder.Build(telemetry, new TelemetryTimeRange(0.2, 0.6));

        var front = traceSet.Front;
        Assert.NotNull(front);
        var trace = Assert.Single(front.Strokes);
        Assert.Equal([20, 30, 40, 50], trace.PositionPercent);
        Assert.Equal([200, 300, 400, 500], trace.VelocityMmPerSecond);
    }

    [Fact]
    public void Build_SegmentedAnalysisRangeUsesSegmentDenseOffset()
    {
        var telemetry = CreateTelemetry(
            sampleRate: 10,
            front: CreateSuspension(
                maxTravel: 100,
                travel: [0, 10, 20, 70, 80, 90],
                velocity: [0, 100, 200, 700, 800, 900],
                compressions: [new Stroke { Start = 0, End = 5 }],
                rebounds: [],
                segments:
                [
                    CreateSegment(firstDenseIndex: 0, startSeconds: 0, sampleCount: 3),
                    CreateSegment(firstDenseIndex: 3, startSeconds: 1, sampleCount: 3)
                ]),
            rear: CreateMissingSuspension());

        var front = PositionVelocityTraceBuilder.Build(telemetry, new TelemetryTimeRange(1, 1.3)).Front;

        Assert.NotNull(front);
        var trace = Assert.Single(front.Strokes);
        Assert.Equal([70, 80, 90], trace.PositionPercent);
        Assert.Equal([700, 800, 900], trace.VelocityMmPerSecond);
    }

    [Fact]
    public void Build_AnalysisRangeInsideSegmentGapProducesNoTrace()
    {
        var telemetry = CreateTelemetry(
            sampleRate: 10,
            front: CreateSuspension(
                maxTravel: 100,
                travel: [0, 10, 20, 70, 80, 90],
                velocity: [0, 100, 200, 700, 800, 900],
                compressions: [new Stroke { Start = 0, End = 5 }],
                rebounds: [],
                segments:
                [
                    CreateSegment(firstDenseIndex: 0, startSeconds: 0, sampleCount: 3),
                    CreateSegment(firstDenseIndex: 3, startSeconds: 1, sampleCount: 3)
                ],
                hasGaps: true),
            rear: CreateMissingSuspension());

        var traceSet = PositionVelocityTraceBuilder.Build(telemetry, new TelemetryTimeRange(0.4, 0.8));

        Assert.Null(traceSet.Front);
    }

    [Fact]
    public void Build_StrokeSpanningSegmentGapProducesSeparateTraces()
    {
        var telemetry = CreateTelemetry(
            sampleRate: 10,
            front: CreateSuspension(
                maxTravel: 100,
                travel: [0, 10, 20, 70, 80, 90],
                velocity: [0, 100, 200, 700, 800, 900],
                compressions: [new Stroke { Start = 0, End = 5 }],
                rebounds: [],
                segments:
                [
                    CreateSegment(firstDenseIndex: 0, startSeconds: 0, sampleCount: 3),
                    CreateSegment(firstDenseIndex: 3, startSeconds: 1, sampleCount: 3)
                ],
                hasGaps: true),
            rear: CreateMissingSuspension());

        var front = PositionVelocityTraceBuilder.Build(telemetry, analysisRange: null).Front;

        Assert.NotNull(front);
        Assert.Equal(2, front.Strokes.Count);
        Assert.Equal([0, 10, 20], front.Strokes[0].PositionPercent);
        Assert.Equal([0, 100, 200], front.Strokes[0].VelocityMmPerSecond);
        Assert.Equal([70, 80, 90], front.Strokes[1].PositionPercent);
        Assert.Equal([700, 800, 900], front.Strokes[1].VelocityMmPerSecond);
    }

    [Fact]
    public void Build_FrontOnlyAndRearOnlySessionsProduceSingleSideTrace()
    {
        var frontOnly = PositionVelocityTraceBuilder.Build(
            CreateTelemetry(front: CreateRenderableSuspension(), rear: CreateMissingSuspension()),
            analysisRange: null);
        var rearOnly = PositionVelocityTraceBuilder.Build(
            CreateTelemetry(front: CreateMissingSuspension(), rear: CreateRenderableSuspension()),
            analysisRange: null);

        Assert.NotNull(frontOnly.Front);
        Assert.Null(frontOnly.Rear);
        Assert.Null(rearOnly.Front);
        Assert.NotNull(rearOnly.Rear);
    }

    [Theory]
    [MemberData(nameof(InvalidMaxTravelValues))]
    public void Build_MissingZeroOrNonFiniteMaxTravelProducesNoTrace(double maxTravel)
    {
        var telemetry = CreateTelemetry(
            front: CreateSuspension(
                maxTravel: maxTravel,
                travel: [0, 10, 20],
                velocity: [0, 100, 200],
                compressions: [new Stroke { Start = 0, End = 2 }],
                rebounds: []),
            rear: CreateMissingSuspension());

        var traceSet = PositionVelocityTraceBuilder.Build(telemetry, analysisRange: null);

        Assert.Null(traceSet.Front);
    }

    [Fact]
    public void Build_MissingStrokesEmptyStrokesOrInvalidStrokeIndexesProduceNoTrace()
    {
        var missingStrokes = PositionVelocityTraceBuilder.Build(
            CreateTelemetry(
                front: new Suspension
                {
                    Present = true,
                    MaxTravel = 100,
                    Travel = [0, 10, 20],
                    Velocity = [0, 100, 200],
                    Strokes = null!,
                    TravelBins = [],
                    VelocityBins = [],
                },
                rear: CreateMissingSuspension()),
            analysisRange: null);
        var emptyStrokes = PositionVelocityTraceBuilder.Build(
            CreateTelemetry(
                front: CreateSuspension(
                    maxTravel: 100,
                    travel: [0, 10, 20],
                    velocity: [0, 100, 200],
                    compressions: [],
                    rebounds: []),
                rear: CreateMissingSuspension()),
            analysisRange: null);
        var invalidIndexes = PositionVelocityTraceBuilder.Build(
            CreateTelemetry(
                front: CreateSuspension(
                    maxTravel: 100,
                    travel: [0, 10, 20],
                    velocity: [0, 100, 200],
                    compressions: [new Stroke { Start = 4, End = 5 }],
                    rebounds: []),
                rear: CreateMissingSuspension()),
            analysisRange: null);

        Assert.Null(missingStrokes.Front);
        Assert.Null(emptyStrokes.Front);
        Assert.Null(invalidIndexes.Front);
    }

    [Fact]
    public void Build_SkipsNonFiniteSamplesAndDropsStrokesWithFewerThanTwoValidPoints()
    {
        var telemetry = CreateTelemetry(
            front: CreateSuspension(
                maxTravel: 100,
                travel: [0, double.NaN, 40, 60, 80, 100],
                velocity: [0, 100, 200, double.PositiveInfinity, 400, 500],
                compressions:
                [
                    new Stroke { Start = 0, End = 2 },
                    new Stroke { Start = 3, End = 4 }
                ],
                rebounds: []),
            rear: CreateMissingSuspension());

        var front = PositionVelocityTraceBuilder.Build(telemetry, null).Front;
        Assert.NotNull(front);
        var trace = Assert.Single(front.Strokes);

        Assert.Equal([0, 40], trace.PositionPercent);
        Assert.Equal([0, 200], trace.VelocityMmPerSecond);
    }

    [Fact]
    public void Build_StrokeCountCappingIsDeterministicAndPreservesFirstAndFinalRetainedStroke()
    {
        var strokeCount = 801;
        var travel = Enumerable.Range(0, strokeCount * 2)
            .Select(index => index == strokeCount * 2 - 1 ? 100.0 : index % 100)
            .ToArray();
        var velocity = Enumerable.Range(0, strokeCount * 2)
            .Select(index => (double)index)
            .ToArray();
        var strokes = Enumerable.Range(0, strokeCount)
            .Select(index => new Stroke { Start = index * 2, End = index * 2 + 1 })
            .ToArray();
        var telemetry = CreateTelemetry(
            front: CreateSuspension(100, travel, velocity, strokes, []),
            rear: CreateMissingSuspension());

        var front = PositionVelocityTraceBuilder.Build(telemetry, null).Front;
        Assert.NotNull(front);
        var traces = front.Strokes;

        Assert.True(traces.Count <= 800);
        Assert.Equal([0, 1], traces[0].VelocityMmPerSecond);
        Assert.Equal([strokeCount * 2 - 2, strokeCount * 2 - 1], traces[^1].VelocityMmPerSecond);
    }

    [Fact]
    public void Build_PointDownsamplingCapsLargeInputsAndPreservesFirstAndFinalPoint()
    {
        var pointCount = 30_001;
        var travel = Enumerable.Range(0, pointCount)
            .Select(index => index == pointCount - 1 ? 100.0 : index % 100)
            .ToArray();
        var velocity = Enumerable.Range(0, pointCount)
            .Select(index => (double)index)
            .ToArray();
        var telemetry = CreateTelemetry(
            front: CreateSuspension(
                maxTravel: 100,
                travel: travel,
                velocity: velocity,
                compressions: [new Stroke { Start = 0, End = pointCount - 1 }],
                rebounds: []),
            rear: CreateMissingSuspension());

        var front = PositionVelocityTraceBuilder.Build(telemetry, null).Front;
        Assert.NotNull(front);
        var trace = Assert.Single(front.Strokes);

        Assert.Equal(15_001, trace.Count);
        Assert.Equal(0, trace.PositionPercent[0]);
        Assert.Equal(0, trace.VelocityMmPerSecond[0]);
        Assert.Equal(2, trace.VelocityMmPerSecond[1]);
        Assert.Equal(pointCount - 3, trace.VelocityMmPerSecond[^2]);
        Assert.Equal(100, trace.PositionPercent[^1]);
        Assert.Equal(pointCount - 1, trace.VelocityMmPerSecond[^1]);
    }

    [Fact]
    public void Build_StrokeCountCappingUsesOnlyRenderableStrokesAndPreservesTheirExactOrder()
    {
        var strokeCount = 802;
        var travel = Enumerable.Range(0, strokeCount * 3)
            .Select(index => index % 100.0)
            .ToArray();
        var velocity = Enumerable.Range(0, strokeCount * 3)
            .Select(index => (double)index)
            .ToArray();
        velocity[4] = double.NaN;
        velocity[5] = double.NaN;
        var strokes = Enumerable.Range(0, strokeCount)
            .Select(index => new Stroke { Start = index * 3, End = index * 3 + 2 })
            .ToArray();
        var telemetry = CreateTelemetry(
            front: CreateSuspension(100, travel, velocity, strokes, []),
            rear: CreateMissingSuspension());

        var front = PositionVelocityTraceBuilder.Build(telemetry, null).Front;
        Assert.NotNull(front);

        var expectedFirstVelocities = new[] { 0.0 }
            .Concat(Enumerable.Range(0, 400).Select(index => 9.0 + index * 6.0))
            .ToArray();
        Assert.Equal(expectedFirstVelocities, front.Strokes.Select(trace => trace.VelocityMmPerSecond[0]));
        Assert.All(front.Strokes, trace => Assert.Equal(3, trace.Count));
    }

    [Fact]
    public void Build_VelocityLimitIsSymmetricRoundedAndHasMinimum()
    {
        var minimum = PositionVelocityTraceBuilder.Build(
            CreateTelemetry(
                front: CreateSuspension(
                    maxTravel: 100,
                    travel: [0, 10],
                    velocity: [0, 250],
                    compressions: [new Stroke { Start = 0, End = 1 }],
                    rebounds: []),
                rear: CreateMissingSuspension()),
            analysisRange: null);
        var rounded = PositionVelocityTraceBuilder.Build(
            CreateTelemetry(
                front: CreateSuspension(
                    maxTravel: 100,
                    travel: [0, 10],
                    velocity: [0, -1251],
                    compressions: [new Stroke { Start = 0, End = 1 }],
                    rebounds: []),
                rear: CreateMissingSuspension()),
            analysisRange: null);

        Assert.Equal(1000, minimum.VelocityLimitMmPerSecond);
        Assert.Equal(1500, rounded.VelocityLimitMmPerSecond);
    }

    [Fact]
    public void Build_HeatmapHoldsPercentOfSidePointsPerPositionAndVelocityBin()
    {
        var telemetry = CreateTelemetry(
            front: CreateSuspension(
                maxTravel: 100,
                travel: [0, 0, 99],
                velocity: [-1000, -1000, 999],
                compressions: [new Stroke { Start = 0, End = 2 }],
                rebounds: []),
            rear: CreateMissingSuspension());

        var front = PositionVelocityTraceBuilder.Build(telemetry, analysisRange: null).Front;

        Assert.NotNull(front);
        var heatmap = front.Heatmap;
        Assert.Equal(200.0 / 3, heatmap[0, 0], 6);
        Assert.Equal(100.0 / 3, heatmap[PositionVelocityTraceBuilder.HeatmapVelocityBinCount - 1, 99], 6);
        Assert.True(double.IsNaN(heatmap[50, 50]));
    }

    private static TelemetryData CreateTelemetry(
        Suspension front,
        Suspension rear,
        int sampleRate = 100) =>
        new()
        {
            Metadata = new Metadata
            {
                SampleRate = sampleRate,
                Timestamp = 1000,
                Duration = Math.Max(front.Travel.Length, rear.Travel.Length) / (double)sampleRate
            },
            Front = front,
            Rear = rear,
            Airtimes = [],
            Markers = [],
        };

    public static TheoryData<double> InvalidMaxTravelValues() =>
    [
        0,
        double.NaN,
        double.PositiveInfinity
    ];

    private static Suspension CreateRenderableSuspension() =>
        CreateSuspension(
            maxTravel: 100,
            travel: [0, 25, 50],
            velocity: [0, 500, 1000],
            compressions: [new Stroke { Start = 0, End = 2 }],
            rebounds: []);

    private static Suspension CreateSuspension(
        double maxTravel,
        double[] travel,
        double[] velocity,
        Stroke[]? compressions = null,
        Stroke[]? rebounds = null,
        ProcessedSuspensionSegment[]? segments = null,
        bool hasGaps = false) =>
        new()
        {
            Present = true,
            MaxTravel = maxTravel,
            Travel = [.. (travel)],
            Velocity = [.. (velocity)],
            Strokes = new Strokes { Compressions = [.. (compressions ?? [])], Rebounds = [.. (rebounds ?? [])] },
            TravelBins = [],
            VelocityBins = [],
            Segments = [.. (segments ?? [])],
            HasGaps = hasGaps
        };

    private static ProcessedSuspensionSegment CreateSegment(
        int firstDenseIndex,
        double startSeconds,
        int sampleCount) =>
        new()
        {
            FirstDenseIndex = firstDenseIndex,
            StartSeconds = startSeconds,
            SampleCount = sampleCount
        };

    private static Suspension CreateMissingSuspension() =>
        new()
        {
            Present = false,
            MaxTravel = null,
            Travel = [],
            Velocity = [],
            Strokes = new Strokes { Compressions = [], Rebounds = [] },
            TravelBins = [],
            VelocityBins = [],
        };
}