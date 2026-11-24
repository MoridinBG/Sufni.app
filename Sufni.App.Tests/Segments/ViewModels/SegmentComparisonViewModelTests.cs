using System.Collections.Immutable;
using Sufni.App.Terrain.Models;
using Sufni.App.Shared.Models;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Sessions.Signals.Views.Controls;
using Sufni.App.Tests.TestSupport.Doubles;
using System.Reactive.Subjects;
using NSubstitute;
using Sufni.App.Bikes.Models;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Segments.ViewModels;

public class SegmentComparisonViewModelTests
{
    private static readonly TelemetryTimeRange AnalysisRange = new(2, 5);

    private readonly ISegmentCoordinator coordinator = Substitute.For<ISegmentCoordinator>();
    private readonly TelemetryData telemetry = TestSegmentMatches.Telemetry();
    private readonly List<(double Start, double End)> visibleRanges = [];
    private readonly List<FailurePresentation> errors = [];
    private readonly List<string> notifications = [];
    private readonly BehaviorSubject<SegmentComparisonSessionState> sessionState = new(SegmentComparisonSessionState.Empty);

    [Fact]
    public void MatchCommand_CannotExecute_WithoutAnAnalysisRange()
    {
        using var comparison = CreateComparison();

        sessionState.OnNext(State() with { AnalysisRange = null });

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
        coordinator.MatchAsync(Arg.Any<SegmentMatchSource>(), Arg.Any<CancellationToken>()).Returns(new SegmentMatchResult.NoTrack());

        await comparison.MatchCommand.ExecuteAsync(null);

        Assert.Single(errors);
        Assert.Empty(comparison.TravelRows);
    }

    [Fact]
    public async Task SessionState_WithAnotherAnalysisRange_DiscardsTheMatches()
    {
        using var comparison = CreateComparison();
        MatchReturns(Comparison(TestSegmentMatches.Match(10, 13)));
        await comparison.MatchCommand.ExecuteAsync(null);

        sessionState.OnNext(State() with { AnalysisRange = new TelemetryTimeRange(3, 6) });

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
        coordinator.MatchAsync(Arg.Any<SegmentMatchSource>(), Arg.Any<CancellationToken>()).Returns(new SegmentMatchResult.Matched(
            matches,
            new SegmentComparisonPage(matches.Take(SegmentComparisonPage.Size).Select(Comparison).ToArray(), [])));
        coordinator.LoadComparisonPageAsync(
                Arg.Is<IReadOnlyList<SegmentMatch>>(page => page.SequenceEqual(new[] { last })),
                Arg.Any<double>(),
                Arg.Any<CancellationToken>())
            .Returns(new SegmentComparisonPageResult.Loaded(new SegmentComparisonPage([next], [])));
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
        Assert.Single(errors);
    }

    [Fact]
    public async Task SessionState_WithOtherDampingCutoffs_RebuildsTheAnalysisComparison()
    {
        using var comparison = CreateComparison();
        var compared = new SegmentComparison(TestSegmentMatches.Match(10, 13), 30, [],
            [new SegmentComparisonSeries("Front", "m/s", SegmentComparisonSeriesRole.FrontVelocity,
                [10, 11, 12, 13], [0.05, 0.10, 0.20, -0.10])], [], [], [], [], new TelemetryTimeRange(10, 13));
        MatchReturns(compared);
        DetailReturns(compared);
        await comparison.MatchCommand.ExecuteAsync(null);
        CompareAction(comparison.VelocityRows[0]).Command!.Execute(null);
        Assert.Equal(25, comparison.AnalysisComparison!.Metrics.Single(metric =>
            metric.Side == SuspensionType.Front && metric.Band == Sufni.App.Sessions.Models.DampingBand.Hsc).MatchValue);

        sessionState.OnNext(State() with
        {
            DampingSpeedCutoffs = new DampingSpeedCutoffs(new DampingSpeedCutoffSide(100, 100), DampingSpeedCutoffSide.Default),
        });

        Assert.NotNull(comparison.AnalysisComparison);
        Assert.Equal(50, comparison.AnalysisComparison.Metrics.Single(metric =>
            metric.Side == SuspensionType.Front && metric.Band == Sufni.App.Sessions.Models.DampingBand.Hsc).MatchValue);
        Assert.Single(comparison.VelocityRows);
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
    public async Task LeavingComparisonScopeRemovesTheTerrainRows()
    {
        using var vm = CreateComparison(new SegmentTerrainComparison.Compared([RoughRow()]));
        var compared = Comparison(TestSegmentMatches.Match(10, 13));
        MatchReturns(compared);
        DetailReturns(compared);
        await vm.MatchCommand.ExecuteAsync(null);
        await ((IAsyncRelayCommand)CompareAction(vm.TravelRows[0]).Command!).ExecuteAsync(null);
        Assert.Single(vm.TerrainTable!.Rows);

        sessionState.OnNext(sessionState.Value with { Scope = AnalysisScope.For(new TerrainClassFilter().Toggle(RoughnessClass.Calm)) });
        Assert.Null(vm.TerrainTable);
        Assert.Single(vm.TravelRows);
    }

    [Fact]
    public async Task ModeToggleKeepsTheAnalysisComparisonVisibleWhileItRebuilds()
    {
        var vm = CreateComparison();
        var compared = Comparison(TestSegmentMatches.Match(10, 13));
        MatchReturns(compared);
        DetailReturns(compared);
        await vm.MatchCommand.ExecuteAsync(null);
        await ((IAsyncRelayCommand)CompareAction(vm.TravelRows[0]).Command!).ExecuteAsync(null);
        var shown = vm.AnalysisComparison;
        var rebuilt = new TaskCompletionSource<SegmentAnalysisComparisonResult>();
        coordinator.BuildAnalysisComparisonAsync(Arg.Any<SegmentComparison>(), Arg.Any<SegmentComparisonDetail>(),
                Arg.Any<TravelDistributionMode>(), Arg.Any<VelocityAverageMode>(), Arg.Any<DampingSpeedCutoffs>(),
                Arg.Any<Sufni.App.Sessions.Models.SessionDampingPercentages>(), Arg.Any<CancellationToken>())
            .Returns(rebuilt.Task);

        sessionState.OnNext(sessionState.Value with { TravelDistributionMode = TravelDistributionMode.DynamicSag });

        Assert.NotNull(shown);
        Assert.Same(shown, vm.AnalysisComparison);
        rebuilt.SetResult(new SegmentAnalysisComparisonResult.Built(shown));
        await vm.DisposeAsync().AsTask().AwaitBoundedAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ModeToggleKeepsTheTerrainComparison()
    {
        await using var vm = CreateComparison(new SegmentTerrainComparison.Compared([RoughRow()]));
        var compared = Comparison(TestSegmentMatches.Match(10, 13));
        MatchReturns(compared);
        DetailReturns(compared);
        await vm.MatchCommand.ExecuteAsync(null);
        await ((IAsyncRelayCommand)CompareAction(vm.TravelRows[0]).Command!).ExecuteAsync(null);
        var table = vm.TerrainTable;

        sessionState.OnNext(sessionState.Value with { VelocityAverageMode = VelocityAverageMode.StrokePeakAveraged });

        Assert.NotNull(table);
        Assert.Same(table, vm.TerrainTable);
    }

    [Fact]
    public async Task FailedTerrainComparisonKeepsTheAnalysisComparison()
    {
        await using var vm = CreateComparison(new SegmentTerrainComparison.Failed());
        var compared = Comparison(TestSegmentMatches.Match(10, 13));
        MatchReturns(compared);
        DetailReturns(compared);
        await vm.MatchCommand.ExecuteAsync(null);

        await ((IAsyncRelayCommand)CompareAction(vm.TravelRows[0]).Command!).ExecuteAsync(null);

        Assert.True(vm.HasAnalysisComparison);
        Assert.True(vm.TerrainTable!.HasMessage);
        Assert.False(vm.TerrainTable.HasRows);
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

    [Fact]
    public async Task CompareAnalysis_FailureKeepsDiagnosticsSeparateFromSummary()
    {
        await using var comparison = CreateComparison();
        var compared = Comparison(TestSegmentMatches.Match(10, 13));
        MatchReturns(compared);
        coordinator.LoadComparisonDetailAsync(compared, Arg.Any<CancellationToken>())
            .Returns(new SegmentComparisonDetailResult.Failed(SegmentComparisonFailureReason.Detail, "reader diagnostic"));
        await comparison.MatchCommand.ExecuteAsync(null);

        await ((IAsyncRelayCommand)CompareAction(comparison.TravelRows[0]).Command!).ExecuteAsync(null);

        var failure = Assert.Single(errors);
        Assert.Equal("reader diagnostic", failure.Details);
        Assert.DoesNotContain(failure.Details!, failure.Summary);
        Assert.False(comparison.HasAnalysisComparison);
        Assert.Single(comparison.TravelRows);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClassFilterSupersedesLoadedAndPendingAnalysisComparisons(bool finishFirst)
    {
        await using var vm = CreateComparison();
        var compared = Comparison(TestSegmentMatches.Match(10, 13));
        MatchReturns(compared);
        var result = new TaskCompletionSource<SegmentComparisonDetailResult>();
        coordinator.LoadComparisonDetailAsync(compared, Arg.Any<CancellationToken>()).Returns(result.Task);
        await vm.MatchCommand.ExecuteAsync(null);
        var load = ((IAsyncRelayCommand)CompareAction(vm.TravelRows[0]).Command!).ExecuteAsync(null);
        Assert.True(CompareAction(vm.TravelRows[0]).IsChecked);
        var detail = new SegmentComparisonDetailResult.Loaded(SegmentComparisonBuilder.BuildDetail(telemetry, []));
        if (finishFirst)
        {
            result.SetResult(detail);
            await load.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
            Assert.True(vm.HasAnalysisComparison);
        }
        var filter = AnalysisScope.For(new TerrainClassFilter().Toggle(RoughnessClass.Rough));
        sessionState.OnNext(sessionState.Value with { Scope = filter });
        if (!finishFirst) result.SetResult(detail);
        await load.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.HasAnalysisComparison);
        Assert.False(CompareAction(vm.TravelRows[0]).IsChecked);
    }

    [Fact]
    public async Task DisposeAsync_CancelsAndDrainsDetailLoadWithoutPublishingLateFailure()
    {
        var comparison = CreateComparison();
        var compared = Comparison(TestSegmentMatches.Match(10, 13));
        MatchReturns(compared);
        var completion = new TaskCompletionSource<SegmentComparisonDetailResult>();
        CancellationToken token = default;
        coordinator.LoadComparisonDetailAsync(compared, Arg.Any<CancellationToken>()).Returns(call =>
        {
            token = call.Arg<CancellationToken>();
            return completion.Task;
        });
        await comparison.MatchCommand.ExecuteAsync(null);
        var loading = ((IAsyncRelayCommand)CompareAction(comparison.TravelRows[0]).Command!).ExecuteAsync(null);

        var closing = comparison.DisposeAsync().AsTask();
        Assert.True(token.IsCancellationRequested);
        Assert.False(closing.IsCompleted);
        completion.SetResult(new SegmentComparisonDetailResult.Failed(SegmentComparisonFailureReason.Detail, "late failure"));
        await closing.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await loading.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(errors);
        Assert.False(comparison.HasAnalysisComparison);
    }

    [Fact]
    public async Task ChangedRange_RejectsPendingComparisonPage()
    {
        await using var comparison = CreateComparison();
        var compared = Comparison(TestSegmentMatches.Match(10, 13));
        coordinator.MatchAsync(Arg.Any<SegmentMatchSource>(), Arg.Any<CancellationToken>())
            .Returns(new SegmentMatchResult.Matched(Enumerable.Repeat(compared.Match, 11).ToArray(), new SegmentComparisonPage([compared], [])));
        var completion = new TaskCompletionSource<SegmentComparisonPageResult>();
        coordinator.LoadComparisonPageAsync(Arg.Any<IReadOnlyList<SegmentMatch>>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(completion.Task);
        await comparison.MatchCommand.ExecuteAsync(null);
        var loading = comparison.NextPageCommand.ExecuteAsync(null);

        sessionState.OnNext(State() with { AnalysisRange = new TelemetryTimeRange(3, 6) });
        completion.SetResult(new SegmentComparisonPageResult.Loaded(new SegmentComparisonPage([compared], [])));
        await loading.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(comparison.TravelRows);
        Assert.Equal(0, comparison.MatchCount);
    }

    [Fact]
    public async Task NewMatchRemovesBothTrailRowsBeforeItsResultArrives()
    {
        await using var vm = CreateComparison();
        TrailMatches((10, 13), (6, 12));
        TestSegmentComparisons.WithTrailAverage(coordinator);
        await vm.MatchCommand.ExecuteAsync(null);
        Assert.Equal(SegmentComparisonSeriesRole.FrameImu, Assert.Single(Assert.Single(vm.RoughnessRows).Series).Role);
        Assert.Equal(SegmentComparisonSeriesRole.Grade, Assert.Single(Assert.Single(vm.GradeRows).Series).Role);

        var second = new TaskCompletionSource<SegmentMatchResult>();
        coordinator.MatchAsync(Arg.Any<SegmentMatchSource>(), Arg.Any<CancellationToken>()).Returns(second.Task);
        var matching = vm.MatchCommand.ExecuteAsync(null);
        Assert.Empty(vm.RoughnessRows);
        Assert.Empty(vm.GradeRows);
        Assert.Equal(2, vm.TravelRows.Count);
        second.SetResult(new SegmentMatchResult.Cancelled());
        await matching;
        Assert.Null(vm.TrailAverage);
        Assert.Equal(2, vm.TravelRows.Count);
    }

    [Fact]
    public async Task ClearingWhileAverageIsPendingDiscardsItsLateResult()
    {
        await using var vm = CreateComparison();
        TrailMatches((10, 13), (6, 12));
        var result = new TaskCompletionSource<SegmentTrailAverage>();
        coordinator.BuildTrailAverageAsync(Arg.Any<SegmentTerrainSource>(), Arg.Any<TelemetryTimeRange>(),
                Arg.Any<IReadOnlyList<SegmentMatch>>(), Arg.Any<CancellationToken>())
            .Returns(result.Task);
        var matching = vm.MatchCommand.ExecuteAsync(null);
        vm.Clear();
        result.SetResult(new SegmentTrailAverage.Averaged(new(3, []), [], []));
        await matching.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.Null(vm.TrailAverage);
    }

    [Fact]
    public async Task SingleMatchShowsTheCoordinatorsTooFewRunsResult()
    {
        await using var vm = CreateComparison();
        TrailMatches((10, 13));
        coordinator.BuildTrailAverageAsync(Arg.Any<SegmentTerrainSource>(), Arg.Any<TelemetryTimeRange>(),
                Arg.Any<IReadOnlyList<SegmentMatch>>(), Arg.Any<CancellationToken>())
            .Returns(new SegmentTrailAverage.TooFewRuns(2));
        await vm.MatchCommand.ExecuteAsync(null);
        Assert.Equal(2, Assert.IsType<SegmentTrailAverage.TooFewRuns>(vm.TrailAverage).RunCount);
        Assert.Empty(vm.RoughnessRows);
        Assert.Single(vm.TravelRows);
    }

    [Fact]
    public async Task SourceProfileArrivingAfterAMatchStartsTheTrailAverage()
    {
        await using var vm = CreateComparison();
        MatchReturns(Comparison(TestSegmentMatches.Match(10, 13)), Comparison(TestSegmentMatches.Match(6, 12)));
        TestSegmentComparisons.WithTrailAverage(coordinator);
        await vm.MatchCommand.ExecuteAsync(null);
        Assert.Empty(vm.RoughnessRows);

        sessionState.OnNext(sessionState.Value with { Terrain = TerrainSource() });

        Assert.Single(vm.RoughnessRows);
        Assert.Single(vm.GradeRows);
    }

    [Fact]
    public async Task FailedTrailAverageShowsAStatusAndKeepsTheMatchedRows()
    {
        await using var vm = CreateComparison();
        TrailMatches((10, 13), (6, 12));
        coordinator.BuildTrailAverageAsync(Arg.Any<SegmentTerrainSource>(), Arg.Any<TelemetryTimeRange>(),
                Arg.Any<IReadOnlyList<SegmentMatch>>(), Arg.Any<CancellationToken>())
            .Returns(new SegmentTrailAverage.Failed());

        await vm.MatchCommand.ExecuteAsync(null);

        Assert.NotNull(vm.TrailAverageStatus);
        Assert.Empty(vm.RoughnessRows);
        Assert.Equal(2, vm.TravelRows.Count);
    }

    private void TrailMatches(params (int Start, int End)[] ranges)
    {
        sessionState.OnNext(State() with { Terrain = TerrainSource() });
        MatchReturns(ranges.Select(r => Comparison(TestSegmentMatches.Match(r.Start, r.End))).ToArray());
    }

    private static SegmentTerrainSource TerrainSource() => new(Guid.NewGuid(), TerrainProfileKey.From(TestSegmentMatches.Token),
        TerrainProfiles.Classified(TerrainProfiles.Window(0, RoughnessClass.Calm, RoughnessSource.FrameOnly)), TerrainBaselineIdentity.Empty);

    private static TerrainComparisonRow RoughRow()
    {
        var measurable = new ComparisonRatio(ResponseCellState.WithinRepeatRange, 1.01);
        return new(RoughnessClass.Rough, 500, new(0.97, 1.01, 1.04),
            Enum.GetValues<ResponseTarget>().ToImmutableDictionary(t => t, _ => measurable), measurable, measurable);
    }

    private SegmentComparisonViewModel CreateComparison(SegmentTerrainComparison? terrain = null)
    {
        coordinator.LoadComparisonDetailAsync(Arg.Any<SegmentComparison>(), Arg.Any<CancellationToken>())
            .Returns(new SegmentComparisonDetailResult.Unavailable());
        var comparison = new SegmentComparisonViewModel(
            Guid.NewGuid(),
            sessionState,
            TestSegmentComparisons.BuildAnalysisComparisons(coordinator, terrain),
            (start, end) => visibleRanges.Add((start, end)),
            errors.Add,
            notifications.Add,
            id => sessionState.OnNext(sessionState.Value with { Scope = new AnalysisScope.SegmentComparison(id) }),
            () => sessionState.OnNext(sessionState.Value with { Scope = new AnalysisScope.Whole() }));
        sessionState.OnNext(State());
        return comparison;
    }

    private SegmentComparisonSessionState State() => sessionState.Value with
    {
        AnalysisRange = AnalysisRange,
        Telemetry = telemetry,
        TrackTimeline = new TrackTimeRange(1000, 30),
    };

    private SegmentComparison Comparison(SegmentMatch match) =>
        SegmentComparisonBuilder.Build(match, TestSegmentMatches.Telemetry(), telemetry.Metadata.Duration)!;

    private void MatchReturns(params SegmentComparison[] comparisons) => TestSegmentComparisons.MatchReturns(coordinator, comparisons);

    private void DetailReturns(SegmentComparison comparison) => TestSegmentComparisons.DetailReturns(coordinator, comparison);

    private static SignalRowAction CompareAction(SegmentComparisonRowViewModel row) =>
        row.HeaderActions.Single(action => action.Id == "compare-analysis");

    private static SignalRowAction RemoveAction(SegmentComparisonRowViewModel row) =>
        row.HeaderActions.Single(action => action.Id == "remove");
}
