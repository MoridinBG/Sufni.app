using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Sufni.App.Bikes.Models;
using Sufni.App.Shared.Common;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

public class RecordedSessionEditorActionsTests
{
    [Fact]
    public void LoadedAnalysisAvailability_DerivesFromTelemetryWithoutCachedPresentation()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedAnalysisPresentationState>();
        using var subscription = driver.Controller.State
            .Select(state => state.Presentation.Analysis)
            .Subscribe(observed.Add);

        driver.PublishTelemetry(TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: false));

        var analysis = observed[^1];
        Assert.Equal(SurfaceStateKind.Ready, analysis.FrontAnalysis.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, analysis.RearAnalysis.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, analysis.CompressionBalance.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, analysis.ReboundBalance.Kind);
    }

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
