using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

public class RecordedSessionEditorActionsTests
{
    [Fact]
    public void AnalysisRangeBoundaries_NormalizeToTelemetryDuration()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedSessionEditorIntentState>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.Intent));
        var telemetry = TestTelemetryData.CreateMinimal(duration: 10.0);

        driver.PublishTelemetry(telemetry);
        driver.Actions.SetAnalysisRangeBoundary(double.NaN);
        driver.Actions.SetAnalysisRangeBoundary(double.PositiveInfinity);
        driver.Actions.SetAnalysisRangeStartBoundary(12.0);
        driver.Actions.SetAnalysisRangeEndBoundary(-2.0);

        var final = observed[^1];
        Assert.Equal(new TelemetryTimeRange(0.0, 10.0), final.AnalysisRange);
        Assert.Null(final.PendingAnalysisRangeBoundary);
    }

    [Fact]
    public void DampingSpeedCutoffs_AreClampedBeforeTheyReachEditorState()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<DampingSpeedCutoffs>();
        using var subscription = driver.Controller.State
            .Select(state => state.Intent.DampingSpeedCutoffs)
            .Subscribe(observed.Add);

        driver.Actions.SetDampingSpeedCutoffs(new DampingSpeedCutoffs(
            new DampingSpeedCutoffSide(-10, DampingSpeedCutoffs.MaximumMmPerSecond + 10),
            new DampingSpeedCutoffSide(125, 250)));

        var cutoffs = observed[^1];
        Assert.Equal(DampingSpeedCutoffs.MinimumMmPerSecond, cutoffs.Front.CompressionMmPerSecond);
        Assert.Equal(DampingSpeedCutoffs.MaximumMmPerSecond, cutoffs.Front.ReboundMmPerSecond);
        Assert.Equal(125, cutoffs.Rear.CompressionMmPerSecond);
        Assert.Equal(250, cutoffs.Rear.ReboundMmPerSecond);
    }

    [Fact]
    public void DirtyBaselineTracking_EmitsEditorBaselineUpdateForEachVisibleChange()
    {
        using var changes = new Subject<Unit>();
        var effects = new List<RecordedSessionEditorEffect>();
        using var subscription = RecordedSessionEditorEffects.DirtyBaselineTracking(changes)
            .Subscribe(effects.Add);

        changes.OnNext(Unit.Default);
        changes.OnNext(Unit.Default);

        Assert.Collection(
            effects,
            effect => Assert.IsType<RecordedSessionEditorEffect.UpdateDirtyBaseline>(effect),
            effect => Assert.IsType<RecordedSessionEditorEffect.UpdateDirtyBaseline>(effect));
    }

    [Fact]
    public void ExtensionHostPublication_PublishesProjectedState_OnEditorAndRuntimeChanges()
    {
        using var states = new Subject<RecordedSessionEditorState>();
        using var runtimes = new Subject<RecordedSessionHostRuntimeState>();
        var timeline = new SessionTimelineLinkViewModel();
        var effects = new List<RecordedSessionEditorEffect.PublishExtensionHostState>();
        using var subscription = RecordedSessionEditorEffects.ExtensionHostPublication(states, runtimes, timeline)
            .OfType<RecordedSessionEditorEffect.PublishExtensionHostState>()
            .Subscribe(effects.Add);
        var session = TestSnapshots.Session(
            name: "trail run",
            hasProcessedData: true) with
        {
            DurationSeconds = 42,
        };
        var telemetry = TestTelemetryData.CreateProcessed();
        var analysisRange = new TelemetryTimeRange(1, 2);
        var nextAnalysisRange = new TelemetryTimeRange(3, 4);
        var trackTimeline = new TrackTimeRange(10, 20);
        var percentages = new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8);
        var cutoffs = DampingSpeedCutoffs.FromValues(110, 220, 330, 440);
        var initial = RecordedSessionEditorState.CreateInitial();
        var state = initial with
        {
            Session = session,
            TelemetryData = telemetry,
            TrackTimelineContext = trackTimeline,
            Intent = initial.Intent with
            {
                AnalysisRange = analysisRange,
                DampingSpeedCutoffs = cutoffs,
                SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged,
                SelectedTravelDistributionMode = TravelDistributionMode.DynamicSag,
            },
            Presentation = initial.Presentation with
            {
                DampingPercentages = percentages,
            },
        };
        var runtime = new RecordedSessionHostRuntimeState(
            session.Id,
            ViewLoaded: true,
            IsActive: false,
            PendingTimelineAlignmentMark: null);

        states.OnNext(state);
        runtimes.OnNext(runtime);

        var published = Assert.Single(effects).State;
        Assert.Equal(session.Id, published.Identity.SessionId);
        Assert.Equal(session.Name, published.Identity.Name);
        Assert.True(published.Identity.IsLoaded);
        Assert.False(published.Identity.IsActive);
        Assert.Equal(analysisRange, published.Selection.AnalysisRange);
        Assert.Equal(trackTimeline, published.Timeline.TrackTimelineContext);
        Assert.Equal(telemetry.Metadata.Duration, published.Timeline.TelemetryDurationSeconds);
        Assert.Same(timeline, published.Timeline.Timeline);
        Assert.Equal(percentages, published.Analysis.DampingPercentages);
        Assert.Equal(cutoffs, published.Analysis.DampingSpeedCutoffs);
        Assert.Equal(VelocityAverageMode.StrokePeakAveraged, published.Analysis.VelocityAverageMode);
        Assert.Equal(TravelDistributionMode.DynamicSag, published.Analysis.TravelDistributionMode);

        runtimes.OnNext(runtime with { IsActive = true });
        states.OnNext(state with { Intent = state.Intent with { AnalysisRange = nextAnalysisRange } });

        Assert.True(effects[1].State.Identity.IsActive);
        Assert.Equal(nextAnalysisRange, effects[^1].State.Selection.AnalysisRange);
    }

    [Fact]
    public void StateController_DisposeStopsSourceSubscription()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var controller = driver.Controller;
        var observed = new List<RecordedSessionEditorState>();
        using var subscription = controller.State.Subscribe(observed.Add);

        driver.PageCounts.OnNext(3);
        driver.Actions.SelectPageIndex(1);
        controller.Dispose();
        driver.Actions.SelectPageIndex(2);

        Assert.Collection(
            observed,
            state => Assert.Equal(0, state.Intent.SelectedPageIndex),
            state => Assert.Equal(1, state.Intent.SelectedPageIndex));
    }
}
