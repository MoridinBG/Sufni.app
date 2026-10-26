using NSubstitute;

using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.TestSupport.Async;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.Doubles;

internal static class TestSegmentComparisons
{
    public static ISegmentComparisonFactory CreateFactory(ISegmentCoordinator? segmentCoordinator = null) =>
        new SegmentComparisonFactory(
            segmentCoordinator ?? Substitute.For<ISegmentCoordinator>(),
            new InlineBackgroundTaskRunner());

    public static SegmentComparisonViewModel Create() =>
        CreateFactory().Create(Guid.NewGuid(), (_, _) => { }, _ => { }, _ => { });

    /// <summary>
    /// A comparison that shows one match; its compare action loads the matched
    /// session's analysis.
    /// </summary>
    public static async Task<SegmentComparisonViewModel> CreateWithMatchAsync()
    {
        var coordinator = Substitute.For<ISegmentCoordinator>();
        var telemetry = TestSegmentMatches.Telemetry();
        var comparison = SegmentComparisonBuilder.Build(TestSegmentMatches.Match(10, 13), telemetry, telemetry.Metadata.Duration)!;
        coordinator.MatchAsync(Arg.Any<SegmentMatchSource>()).Returns(new SegmentMatchResult.Matched(
            [comparison.Match],
            new SegmentComparisonPage([comparison], [])));
        coordinator.LoadComparisonDetailAsync(comparison, Arg.Any<CancellationToken>())
            .Returns(SegmentComparisonBuilder.BuildDetail(telemetry, [new TrackPoint(0, 0, 0, null), new TrackPoint(1, 1, 1, null)]));
        var segmentComparison = CreateFactory(coordinator).Create(Guid.NewGuid(), (_, _) => { }, _ => { }, _ => { });
        segmentComparison.SetSessionState(SegmentComparisonSessionState.Empty with
        {
            AnalysisRange = new TelemetryTimeRange(2, 5),
            Telemetry = telemetry,
            TrackTimeline = new TrackTimeRange(1000, telemetry.Metadata.Duration),
        });
        await segmentComparison.MatchCommand.ExecuteAsync(null);
        return segmentComparison;
    }
}
