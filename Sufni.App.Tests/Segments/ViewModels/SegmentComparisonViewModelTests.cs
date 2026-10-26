using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.App.ExtensionHost.TestSupport.Async;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Segments.ViewModels;

public class SegmentComparisonViewModelTests
{
    private static readonly TelemetryTimeRange AnalysisRange = new(2, 5);

    private readonly ISegmentCoordinator coordinator = Substitute.For<ISegmentCoordinator>();
    private readonly TelemetryData telemetry = TestSegmentMatches.Telemetry();
    private readonly List<(double Start, double End)> visibleRanges = [];
    private readonly List<string> errors = [];
    private readonly List<string> notifications = [];

    [Fact]
    public void MatchCommand_CannotExecute_WithoutAnAnalysisRange()
    {
        using var comparison = CreateComparison();

        comparison.SetSessionState(State() with { AnalysisRange = null });

        Assert.False(comparison.MatchCommand.CanExecute(null));
    }

    [Fact]
    public async Task Match_ShowsTheFirstPageAsRowsOfTheMatchedSignals()
    {
        using var comparison = CreateComparison();
        var compared = Comparison(TestSegmentMatches.Match(10, 13));
        MatchReturns(compared);

        await comparison.MatchCommand.ExecuteAsync(null);

        Assert.Same(compared, Assert.Single(comparison.TravelRows).Comparison);
        Assert.Same(compared, Assert.Single(comparison.VelocityRows).Comparison);
        Assert.Empty(comparison.ImuRows);
        Assert.Empty(comparison.SpeedRows);
        Assert.Equal(["Found 1 segment match."], notifications);
    }

    [Fact]
    public async Task Match_WithoutTrackPoints_ReportsAnError()
    {
        using var comparison = CreateComparison();
        coordinator.MatchAsync(Arg.Any<SegmentMatchSource>()).Returns(new SegmentMatchResult.NoTrack());

        await comparison.MatchCommand.ExecuteAsync(null);

        Assert.Equal(["Segment matching needs a selected range with at least two track points."], errors);
        Assert.Empty(comparison.TravelRows);
    }

    [Fact]
    public async Task SetSessionState_WithAnotherAnalysisRange_DiscardsTheMatches()
    {
        using var comparison = CreateComparison();
        MatchReturns(Comparison(TestSegmentMatches.Match(10, 13)));
        await comparison.MatchCommand.ExecuteAsync(null);

        comparison.SetSessionState(State() with { AnalysisRange = new TelemetryTimeRange(3, 6) });

        Assert.Empty(comparison.TravelRows);
        Assert.Equal(0, comparison.MatchCount);
    }

    [Fact]
    public async Task NextPage_ShowsTheComparisonsOfTheNextMatches()
    {
        using var comparison = CreateComparison();
        var matches = Enumerable.Range(0, SegmentComparisonPage.Size + 1)
            .Select(index => TestSegmentMatches.Match(index, index + 3))
            .ToArray();
        var last = matches[^1];
        var next = Comparison(last);
        coordinator.MatchAsync(Arg.Any<SegmentMatchSource>()).Returns(new SegmentMatchResult.Matched(
            matches,
            new SegmentComparisonPage(matches.Take(SegmentComparisonPage.Size).Select(Comparison).ToArray(), [])));
        coordinator.LoadComparisonPageAsync(
                Arg.Is<IReadOnlyList<SegmentMatch>>(page => page.SequenceEqual(new[] { last })),
                Arg.Any<double>(),
                Arg.Any<CancellationToken>())
            .Returns(new SegmentComparisonPage([next], []));
        await comparison.MatchCommand.ExecuteAsync(null);

        await comparison.NextPageCommand.ExecuteAsync(null);

        Assert.Same(next, Assert.Single(comparison.TravelRows).Comparison);
        Assert.True(comparison.PreviousPageCommand.CanExecute(null));
        Assert.False(comparison.NextPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task CompareAnalysis_BuildsTheAnalysisComparisonAndZoomsTheTimelineToTheRange()
    {
        using var comparison = CreateComparison();
        var compared = Comparison(TestSegmentMatches.Match(10, 13));
        MatchReturns(compared);
        DetailReturns(compared);
        await comparison.MatchCommand.ExecuteAsync(null);

        CompareAction(comparison.TravelRows[0]).Command!.Execute(null);

        Assert.Same(compared, comparison.AnalysisComparison?.Comparison);
        Assert.True(CompareAction(comparison.VelocityRows[0]).IsChecked);
        var (start, end) = Assert.Single(visibleRanges);
        Assert.Equal(1.85 / 30, start, precision: 6);
        Assert.Equal(5.15 / 30, end, precision: 6);
    }

    [Fact]
    public async Task CompareAnalysis_WhenTheMatchedSessionChanged_RemovesItsRowsAndReportsAnError()
    {
        using var comparison = CreateComparison();
        MatchReturns(Comparison(TestSegmentMatches.Match(10, 13)));
        await comparison.MatchCommand.ExecuteAsync(null);

        CompareAction(comparison.TravelRows[0]).Command!.Execute(null);

        Assert.Empty(comparison.TravelRows);
        Assert.Empty(comparison.VelocityRows);
        Assert.Equal(0, comparison.MatchCount);
        Assert.Equal(["The matched session telemetry is not available."], errors);
    }

    [Fact]
    public async Task SetSessionState_WithOtherDampingCutoffs_RebuildsTheAnalysisComparison()
    {
        using var comparison = CreateComparison();
        var compared = Comparison(TestSegmentMatches.Match(10, 13));
        MatchReturns(compared);
        DetailReturns(compared);
        await comparison.MatchCommand.ExecuteAsync(null);
        CompareAction(comparison.TravelRows[0]).Command!.Execute(null);
        var before = comparison.AnalysisComparison;

        comparison.SetSessionState(State() with
        {
            DampingSpeedCutoffs = new DampingSpeedCutoffs(new DampingSpeedCutoffSide(10, 10), DampingSpeedCutoffSide.Default),
        });

        Assert.NotNull(comparison.AnalysisComparison);
        Assert.NotSame(before, comparison.AnalysisComparison);
        Assert.Single(comparison.TravelRows);
    }

    [Fact]
    public async Task ClearAnalysisComparison_StopsComparingAndKeepsTheRows()
    {
        using var comparison = CreateComparison();
        var compared = Comparison(TestSegmentMatches.Match(10, 13));
        MatchReturns(compared);
        DetailReturns(compared);
        await comparison.MatchCommand.ExecuteAsync(null);
        CompareAction(comparison.TravelRows[0]).Command!.Execute(null);

        comparison.ClearAnalysisComparisonCommand.Execute(null);

        Assert.Null(comparison.AnalysisComparison);
        Assert.False(CompareAction(comparison.TravelRows[0]).IsChecked);
    }

    [Fact]
    public async Task RemoveRow_DropsTheMatchWithItsLastRow()
    {
        using var comparison = CreateComparison();
        MatchReturns(Comparison(TestSegmentMatches.Match(10, 13)));
        await comparison.MatchCommand.ExecuteAsync(null);

        RemoveAction(comparison.TravelRows[0]).Command!.Execute(null);

        Assert.Equal(1, comparison.MatchCount);
        Assert.Single(comparison.VelocityRows);

        RemoveAction(comparison.VelocityRows[0]).Command!.Execute(null);

        Assert.Equal(0, comparison.MatchCount);
    }

    private SegmentComparisonViewModel CreateComparison()
    {
        var comparison = new SegmentComparisonViewModel(
            Guid.NewGuid(),
            coordinator,
            new InlineBackgroundTaskRunner(),
            (start, end) => visibleRanges.Add((start, end)),
            errors.Add,
            notifications.Add);
        comparison.SetSessionState(State());
        return comparison;
    }

    private SegmentComparisonSessionState State() => SegmentComparisonSessionState.Empty with
    {
        AnalysisRange = AnalysisRange,
        Telemetry = telemetry,
        TrackTimeline = new TrackTimeRange(1000, 30),
    };

    private SegmentComparison Comparison(SegmentMatch match) =>
        SegmentComparisonBuilder.Build(match, TestSegmentMatches.Telemetry(), telemetry.Metadata.Duration)!;

    private void MatchReturns(SegmentComparison comparison) =>
        coordinator.MatchAsync(Arg.Any<SegmentMatchSource>()).Returns(new SegmentMatchResult.Matched(
            [comparison.Match],
            new SegmentComparisonPage([comparison], [])));

    private void DetailReturns(SegmentComparison comparison) =>
        coordinator.LoadComparisonDetailAsync(comparison, Arg.Any<CancellationToken>())
            .Returns(SegmentComparisonBuilder.BuildDetail(TestSegmentMatches.Telemetry(), matchedTrackPoints: null));

    private static SignalRowAction CompareAction(SegmentComparisonRowViewModel row) =>
        row.HeaderActions.Single(action => action.Id == "compare-analysis");

    private static SignalRowAction RemoveAction(SegmentComparisonRowViewModel row) =>
        row.HeaderActions.Single(action => action.Id == "remove");
}
