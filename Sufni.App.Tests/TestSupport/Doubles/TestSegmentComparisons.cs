using System.Reactive.Subjects;
using Sufni.App.Terrain.Models;
using Sufni.App.Shared.Models;
using Sufni.App.Segments.Models;
using Sufni.App.Bikes.Models;
using Sufni.App.Sessions.Models;
using System.Reactive.Linq;
using NSubstitute;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.Doubles;

internal static class TestSegmentComparisons
{
    public static ISegmentComparisonFactory CreateFactory(ISegmentCoordinator? segmentCoordinator = null, SegmentTerrainComparison? terrain = null) =>
        new SegmentComparisonFactory(BuildAnalysisComparisons(segmentCoordinator ?? Substitute.For<ISegmentCoordinator>(), terrain));

    /// <summary>
    /// Makes the substitute build analysis comparisons with the real builder and return
    /// <paramref name="terrain"/>, or no source profile, as the terrain comparison.
    /// </summary>
    public static ISegmentCoordinator BuildAnalysisComparisons(ISegmentCoordinator coordinator, SegmentTerrainComparison? terrain = null)
    {
        coordinator.BuildAnalysisComparisonAsync(Arg.Any<SegmentComparison>(), Arg.Any<SegmentComparisonDetail>(),
                Arg.Any<TravelDistributionMode>(), Arg.Any<VelocityAverageMode>(), Arg.Any<DampingSpeedCutoffs>(),
                Arg.Any<SessionDampingPercentages>(), Arg.Any<CancellationToken>())
            .Returns(call => new SegmentAnalysisComparisonResult.Built(SegmentAnalysisComparisonBuilder.Build(
                call.Arg<SegmentComparison>(), call.Arg<SegmentComparisonDetail>(), call.Arg<TravelDistributionMode>(),
                call.Arg<VelocityAverageMode>(), call.Arg<DampingSpeedCutoffs>(), call.Arg<SessionDampingPercentages>())));
        coordinator.BuildTerrainComparisonAsync(Arg.Any<SegmentMatch>(), Arg.Any<SegmentTerrainSource?>(),
                Arg.Any<TrailAverage.Averaged?>(), Arg.Any<CancellationToken>())
            .Returns(terrain ?? new SegmentTerrainComparison.Unavailable(SegmentTerrainUnavailable.NoSourceProfile));
        return coordinator;
    }

    public static void MatchReturns(ISegmentCoordinator coordinator, params SegmentComparison[] comparisons) =>
        coordinator.MatchAsync(Arg.Any<SegmentMatchSource>(), Arg.Any<CancellationToken>()).Returns(new SegmentMatchResult.Matched(
            comparisons.Select(c => c.Match).ToArray(), new SegmentComparisonPage(comparisons, [])));

    public static void DetailReturns(ISegmentCoordinator coordinator, SegmentComparison comparison,
        IReadOnlyList<TrackPoint>? matchedTrackPoints = null) =>
        coordinator.LoadComparisonDetailAsync(comparison, Arg.Any<CancellationToken>()).Returns(new SegmentComparisonDetailResult.Loaded(
            SegmentComparisonBuilder.BuildDetail(TestSegmentMatches.Telemetry(), matchedTrackPoints)));

    public static void WithTrailAverage(ISegmentCoordinator coordinator)
    {
        var frame = new SegmentComparisonSeries("Frame", "g", SegmentComparisonSeriesRole.FrameImu, [3, 4], [1.2, 1.4]);
        var grade = new SegmentComparisonSeries("Grade", "°", SegmentComparisonSeriesRole.Grade, [3, 4], [8, 9]);
        coordinator.BuildTrailAverageAsync(Arg.Any<SegmentTerrainSource>(), Arg.Any<TelemetryTimeRange>(),
                Arg.Any<IReadOnlyList<SegmentMatch>>(), Arg.Any<CancellationToken>())
            .Returns(new SegmentTrailAverage.Averaged(new(3, []), [frame], [grade]));
    }

    public static SegmentComparisonViewModel Create() =>
        CreateFactory().Create(Guid.NewGuid(), Observable.Never<SegmentComparisonSessionState>(), (_, _) => { }, _ => { }, _ => { }, _ => { }, () => { });

    /// <summary>
    /// Creates a comparison whose begin and end requests set the scope in <paramref name="states"/>,
    /// as the editor reducer does.
    /// </summary>
    public static SegmentComparisonViewModel Create(ISegmentCoordinator coordinator,
        BehaviorSubject<SegmentComparisonSessionState> states, Action<double, double>? setTimelineVisibleRange = null,
        Action<FailurePresentation>? reportError = null, Action<string>? reportNotification = null) =>
        new SegmentComparisonFactory(coordinator).Create(Guid.NewGuid(), states, setTimelineVisibleRange ?? ((_, _) => { }),
            reportError ?? (_ => { }), reportNotification ?? (_ => { }),
            id => states.OnNext(states.Value with { Scope = new AnalysisScope.SegmentComparison(id) }),
            () => states.OnNext(states.Value with { Scope = new AnalysisScope.Default() }));

    /// <summary>
    /// A comparison that shows one match; its compare action loads the matched
    /// session's analysis.
    /// </summary>
    public static async Task<SegmentComparisonViewModel> CreateWithMatchAsync(SegmentTerrainComparison? terrain = null)
    {
        var coordinator = Substitute.For<ISegmentCoordinator>();
        var telemetry = TestSegmentMatches.Telemetry();
        var comparison = SegmentComparisonBuilder.Build(TestSegmentMatches.Match(10, 13), telemetry, telemetry.Metadata.Duration)!;
        MatchReturns(coordinator, comparison);
        DetailReturns(coordinator, comparison, [new TrackPoint(0, 0, 0, null), new TrackPoint(1, 1, 1, null)]);
        var sessionState = SegmentComparisonSessionState.Empty with
        {
            AnalysisRange = new TelemetryTimeRange(2, 5),
            Telemetry = telemetry,
            TrackTimeline = new TrackTimeRange(1000, telemetry.Metadata.Duration),
        };
        var segmentComparison = Create(BuildAnalysisComparisons(coordinator, terrain),
            new BehaviorSubject<SegmentComparisonSessionState>(sessionState));
        await segmentComparison.MatchCommand.ExecuteAsync(null);
        return segmentComparison;
    }
}
