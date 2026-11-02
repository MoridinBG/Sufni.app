using System.Reactive.Linq;
using Sufni.App.Shared.Common;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

public class RecordedSessionEditorStateControllerTests
{
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
        Assert.Equal(SurfaceStateKind.Ready, state.Presentation.Analysis.FrontAnalysis.Kind);
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
