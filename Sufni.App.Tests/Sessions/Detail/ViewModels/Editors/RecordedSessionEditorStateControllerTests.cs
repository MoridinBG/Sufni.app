using Sufni.App.Sessions.Analysis.Services;
using System.Reactive.Linq;
using Sufni.App.Shared.Common;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;
using Sufni.App.Sessions.Models;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

public class RecordedSessionEditorStateControllerTests
{
    [Fact]
    public void AvailabilityResult_SurvivesTrackOnlyRefresh_AndRangeChangeClearsHighlightsOnce()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var telemetry = TestTelemetryData.CreateMinimal(duration: 10);
        driver.PublishTelemetry(telemetry);
        driver.Actions.SelectAnalysisRange(new DampingRangeSelection(SuspensionType.Front, VelocityAverageMode.SampleAveraged, 0, 0, 0));
        driver.AnalysisAvailability.OnNext(new(AnalysisDataAvailability.Available, AnalysisDataAvailability.Absent,
            AnalysisDataAvailability.Absent, AnalysisDataAvailability.Absent, AnalysisDataAvailability.Absent,
            AnalysisDataAvailability.Absent, AnalysisDataAvailability.Absent, AnalysisDataAvailability.Absent, false));
        driver.SelectionHighlights.OnNext(new([new TelemetryHighlightRange(1, 2, SuspensionType.Front)]));
        var observed = new List<RecordedSessionEditorState>();
        using var subscription = driver.Controller.State.Skip(1).Subscribe(observed.Add);

        driver.PublishLoadedData(telemetryData: telemetry, trackPoints: []);
        var refreshed = Assert.Single(observed);
        Assert.Equal(SurfaceStateKind.Ready, refreshed.Presentation.Analysis.FrontAnalysis.Kind);
        Assert.NotEmpty(refreshed.AnalysisSelection.HighlightRanges);
        observed.Clear();
        driver.Actions.SetAnalysisRange(new(3, 4));
        var ranged = Assert.Single(observed);
        Assert.Equal(SurfaceStateKind.Loading, ranged.Presentation.Analysis.FrontAnalysis.Kind);
        Assert.Empty(ranged.AnalysisSelection.HighlightRanges);
        Assert.NotNull(ranged.AnalysisSelection.ActiveFront);
        Assert.False(ranged.Presentation.Signals.ShowAnalysisSelection);
    }

    [Fact]
    public void RangeChange_ClearsPreviousAnalysisOutputsInTheSameState()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var telemetry = TestTelemetryData.CreateMinimal(duration: 10);
        driver.PublishTelemetry(telemetry);
        driver.DampingPercentages.OnNext(new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8));
        driver.SessionInsights.OnNext(new SessionInsightsResult(SurfacePresentationState.Ready, []));
        var observed = new List<RecordedSessionEditorState>();
        using var subscription = driver.Controller.State.Skip(1).Subscribe(observed.Add);
        var range = new TelemetryTimeRange(telemetry.Metadata.Duration / 4, telemetry.Metadata.Duration / 2);

        driver.Actions.SetAnalysisRange(range);

        var state = Assert.Single(observed);
        Assert.Equal(range, state.Intent.AnalysisRange);
        Assert.Equal(SessionDampingPercentages.Empty, state.Presentation.DampingPercentages);
        Assert.Equal(SessionInsightsResult.Hidden, state.Presentation.SessionInsights);
    }

    [Fact]
    public void Load_PublishesTelemetryRangeAndAvailabilityTogetherOnce()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        driver.Actions.SetAnalysisRange(new TelemetryTimeRange(0, 30));
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: false);
        var observed = new List<RecordedSessionEditorState>();
        using var subscription = driver.Controller.State.Skip(1).Subscribe(observed.Add);

        driver.PublishTelemetry(telemetry);

        var state = Assert.Single(observed);
        Assert.Same(telemetry, state.TelemetryData);
        Assert.Equal(new TelemetryTimeRange(0, Math.Min(30, telemetry.Metadata.Duration)), state.Intent.AnalysisRange);
        Assert.Equal(SurfaceStateKind.Ready, state.Presentation.Signals.Travel.Kind);
        Assert.Equal(SurfaceStateKind.Loading, state.Presentation.Analysis.FrontAnalysis.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, state.Presentation.Analysis.RearAnalysis.Kind);
        Assert.IsType<RecordedSessionLoadPresentation.Loaded>(state.Load);
    }

    [Fact]
    public void ReentrantIntent_FinishesPublishingCurrentStateBeforeTheNext()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        driver.PageCounts.OnNext(3);
        using var effect = driver.Controller.State.Subscribe(state =>
        {
            if (state.Intent.SelectedPageIndex == 1) driver.Actions.SelectPageIndex(2);
        });
        var observed = new List<int>();
        using var subscription = driver.Controller.State.Select(state => state.Intent.SelectedPageIndex).Subscribe(observed.Add);

        driver.Actions.SelectPageIndex(1);

        Assert.Equal([0, 1, 2], observed);
    }
}
