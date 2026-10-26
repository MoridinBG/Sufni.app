using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Segments.Services;

public class SegmentComparisonBuilderTests
{
    private static readonly IReadOnlyList<TrackPoint> MatchedTrack =
    [
        new(19, 0, 0, elevation: null, speed: 1),
        new(20, 1, 0, elevation: null, speed: 2),
        new(21, 2, 0, elevation: null, speed: 3),
        new(22, 3, 0, elevation: null, speed: 4),
        new(23, 4, 0, elevation: null, speed: 5),
    ];

    [Fact]
    public void Build_ProjectsTelemetryAndTrackDataOntoSourceTimeline()
    {
        var match = TestSegmentMatches.Match(20, 22, [new(10, 20), new(12, 22)]);

        var comparison = SegmentComparisonBuilder.Build(
            match,
            TestSegmentMatches.Telemetry(),
            sourceDurationSeconds: 30,
            MatchedTrack,
            new TrackTimeRange(OriginSeconds: 0, DurationSeconds: 30));

        Assert.NotNull(comparison);
        Assert.Same(match, comparison.Match);
        Assert.True(comparison.CanCompareAnalysis);
        var travel = Assert.Single(comparison.TravelSeries);
        Assert.Equal(SegmentComparisonSeriesRole.FrontTravel, travel.Role);
        Assert.Equal(10, travel.SourceSeconds[0], precision: 6);
        Assert.Equal(12, travel.SourceSeconds[^1], precision: 6);
        Assert.Equal(20, travel.Values[0], precision: 6);
        Assert.Equal(22, travel.Values[^1], precision: 6);
        var velocity = Assert.Single(comparison.VelocitySeries);
        Assert.Equal(20, velocity.Values[0], precision: 6);
        Assert.Equal(22, velocity.Values[^1], precision: 6);
        var speedRun = Assert.Single(Assert.Single(comparison.SpeedSeries).Runs!);
        Assert.Equal(7.2, speedRun.Values[0], precision: 6);
        Assert.Equal(14.4, speedRun.Values[^1], precision: 6);
        var airtime = Assert.Single(comparison.Airtimes);
        Assert.Equal(10.5, airtime.StartSeconds, precision: 6);
        Assert.Equal(11.5, airtime.EndSeconds, precision: 6);
        Assert.Equal([20, 21, 22], comparison.MatchedSegmentTrackPoints.Select(point => point.Time));
    }

    [Fact]
    public void BuildDetail_KeepsTheMatchedTelemetryAndTheWholeTrack()
    {
        var telemetry = TestSegmentMatches.Telemetry();

        var detail = SegmentComparisonBuilder.BuildDetail(telemetry, MatchedTrack);

        Assert.Same(telemetry, detail.AnalysisTelemetryData);
        Assert.Equal([19, 20, 21, 22, 23], detail.MatchedSessionTrackPoints.Select(point => point.Time));
    }

    [Fact]
    public void Build_SpeedUsesAvailableRunsWithoutFillingGapsOrDroppingZero()
    {
        IReadOnlyList<TrackPoint> track =
        [
            new(0, 0, 0, elevation: null, speed: 0),
            new(0.1, 0, 0, elevation: null, speed: 1),
            new(0.2, 0, 0, elevation: null, speed: null),
            new(1, 0, 0, elevation: null, speed: 2),
            new(1.1, 0, 0, elevation: null, speed: 3),
        ];

        var comparison = SegmentComparisonBuilder.Build(
            TestSegmentMatches.Match(0, 1.1),
            TestSegmentMatches.Telemetry(),
            sourceDurationSeconds: 2,
            track,
            new TrackTimeRange(OriginSeconds: 0, DurationSeconds: 2));

        Assert.NotNull(comparison);
        var speed = Assert.Single(comparison.SpeedSeries);
        Assert.Empty(speed.SourceSeconds);
        Assert.Empty(speed.Values);
        var runs = Assert.IsAssignableFrom<IReadOnlyList<SegmentComparisonSeriesRun>>(speed.Runs);
        Assert.Equal(2, runs.Count);
        Assert.Equal(0, runs[0].SourceSeconds[0], precision: 6);
        Assert.Equal(0.1, runs[0].SourceSeconds[^1], precision: 6);
        Assert.Equal(0, runs[0].Values[0], precision: 6);
        Assert.Equal(3.6, runs[0].Values[^1], precision: 6);
        Assert.Equal(1, runs[1].SourceSeconds[0], precision: 6);
        Assert.Equal(1.1, runs[1].SourceSeconds[^1], precision: 6);
        Assert.Equal(7.2, runs[1].Values[0], precision: 6);
        Assert.Equal(10.8, runs[1].Values[^1], precision: 6);
        Assert.DoesNotContain(runs.SelectMany(run => run.SourceSeconds), seconds => seconds > 0.1 && seconds < 1);
    }

    [Fact]
    public void Build_WithEquivalentDenseAndSegmentOnlyImu_ProducesSameSeries()
    {
        var match = TestSegmentMatches.Match(0, 0.2);
        var denseTelemetry = TestSegmentMatches.Telemetry();
        denseTelemetry.ImuData = CreateImu(segmented: false);
        var segmentedTelemetry = TestSegmentMatches.Telemetry();
        segmentedTelemetry.ImuData = CreateImu(segmented: true);

        var dense = SegmentComparisonBuilder.Build(match, denseTelemetry, sourceDurationSeconds: 1);
        var segmented = SegmentComparisonBuilder.Build(match, segmentedTelemetry, sourceDurationSeconds: 1);

        Assert.NotNull(dense);
        Assert.NotNull(segmented);
        var denseSeries = Assert.Single(dense.ImuSeries);
        var segmentedSeries = Assert.Single(segmented.ImuSeries);
        Assert.Equal(denseSeries.Role, segmentedSeries.Role);
        var denseRun = Assert.Single(denseSeries.Runs!);
        var segmentedRun = Assert.Single(segmentedSeries.Runs!);
        Assert.Equal(denseRun.SourceSeconds, segmentedRun.SourceSeconds);
        Assert.Equal(denseRun.Values, segmentedRun.Values);
    }

    [Fact]
    public void Build_WithGappedSegmentOnlyImu_UsesSegmentTimesWithoutFillingTheGap()
    {
        var telemetry = TestSegmentMatches.Telemetry();
        telemetry.ImuData = new RawImuData
        {
            SampleRate = 10,
            ActiveLocations = [(byte)ImuLocation.Frame],
            Meta = [new ImuMetaEntry((byte)ImuLocation.Frame, 1000, 1)],
            HasGaps = true,
            Segments =
            [
                new RawImuSegment
                {
                    LocationId = (byte)ImuLocation.Frame,
                    FirstMonotonicDeltaUs = 0,
                    Records =
                    [
                        new ImuRecord(0, 0, 1000, 0, 0, 0),
                        new ImuRecord(2000, 0, 1000, 0, 0, 0),
                    ],
                },
                new RawImuSegment
                {
                    LocationId = (byte)ImuLocation.Frame,
                    FirstMonotonicDeltaUs = 1_000_000,
                    Records =
                    [
                        new ImuRecord(3000, 0, 1000, 0, 0, 0),
                        new ImuRecord(4000, 0, 1000, 0, 0, 0),
                    ],
                },
            ],
        };

        var comparison = SegmentComparisonBuilder.Build(
            TestSegmentMatches.Match(-0.2, 1.3),
            telemetry,
            sourceDurationSeconds: 2);

        Assert.NotNull(comparison);
        var series = Assert.Single(comparison.ImuSeries);
        Assert.Empty(series.SourceSeconds);
        Assert.Empty(series.Values);
        var runs = Assert.IsAssignableFrom<IReadOnlyList<SegmentComparisonSeriesRun>>(series.Runs);
        Assert.Equal(2, runs.Count);
        Assert.Equal(0, runs[0].SourceSeconds[0], precision: 6);
        Assert.Equal(0.1, runs[0].SourceSeconds[^1], precision: 6);
        Assert.Equal(0, runs[0].Values[0], precision: 6);
        Assert.Equal(2, runs[0].Values[^1], precision: 6);
        Assert.Equal(1, runs[1].SourceSeconds[0], precision: 6);
        Assert.Equal(1.1, runs[1].SourceSeconds[^1], precision: 6);
        Assert.Equal(3, runs[1].Values[0], precision: 6);
        Assert.Equal(4, runs[1].Values[^1], precision: 6);
        Assert.DoesNotContain(runs.SelectMany(run => run.SourceSeconds), seconds => seconds > 0.1 && seconds < 1.0);
    }

    [Fact]
    public void Build_WhenTimeMapIsTooSmall_ReturnsNull()
    {
        var match = TestSegmentMatches.Match(20, 22, [new(10, 20)]);

        Assert.Null(SegmentComparisonBuilder.Build(match, TestSegmentMatches.Telemetry(), sourceDurationSeconds: 30));
    }

    private static RawImuData CreateImu(bool segmented)
    {
        var records = new[]
        {
            new ImuRecord(1000, 0, 1000, 0, 0, 0),
            new ImuRecord(2000, 0, 1000, 0, 0, 0),
            new ImuRecord(3000, 0, 1000, 0, 0, 0),
        };
        return new RawImuData
        {
            SampleRate = 10,
            ActiveLocations = [(byte)ImuLocation.Frame],
            Meta = [new ImuMetaEntry((byte)ImuLocation.Frame, 1000, 1)],
            Records = segmented ? [] : [.. records],
            Segments = segmented
                ?
                [
                    new RawImuSegment
                    {
                        LocationId = (byte)ImuLocation.Frame,
                        FirstMonotonicDeltaUs = 0,
                        Records = records,
                    },
                ]
                : [],
        };
    }
}
