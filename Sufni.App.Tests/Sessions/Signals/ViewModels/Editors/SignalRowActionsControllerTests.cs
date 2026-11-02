using System.Reactive.Linq;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Signals.ViewModels.Editors;

public class SignalRowActionsControllerTests : IDisposable
{
    private readonly RecordedSessionEditorStateControllerTestDriver driver = new();
    private RecordedSessionEditorState state = RecordedSessionEditorState.CreateInitial();
    private readonly SignalRowActionsController controller;
    private readonly IDisposable subscription;

    public SignalRowActionsControllerTests()
    {
        controller = new SignalRowActionsController(() => state.Presentation.Signals, driver.Actions);
        subscription = driver.Controller.State.Subscribe(value =>
        {
            state = value;
            controller.ApplyActionStates(value);
        });
    }

    [Fact]
    public void AirtimeActions_FollowLoadedTelemetryPresence()
    {
        Assert.All(Actions(0), action => Assert.False(action.IsVisible));
        var telemetry = TestTelemetryData.CreateMinimal();
        telemetry = telemetry with { Airtimes = [new Airtime { Start = 1, End = 2 }] };

        driver.PublishTelemetry(telemetry);

        Assert.All(Actions(0), action => Assert.True(action.IsVisible));
        driver.PublishLoadedData();
        Assert.All(Actions(0), action => Assert.False(action.IsVisible));
    }

    [Fact]
    public void AirtimeCommand_ChangesOnlyItsSignalAndFollowsLaterState()
    {
        var action = controller.VelocityHeaderActions[0];
        action.Command!.Execute(null);

        Assert.True(state.Presentation.Signals.ShowVelocityAirtime);
        Assert.True(action.IsChecked);
        Assert.False(state.Presentation.Signals.ShowSpeedAirtime);

        driver.Actions.SetSignalPresentation(state.Presentation.Signals with { ShowVelocityAirtime = false });
        Assert.False(action.IsChecked);
    }

    [Fact]
    public void AnalysisActions_FollowSelectionAndProjectTheirToggleIntent()
    {
        Assert.All(Actions(1), action => Assert.False(action.IsEnabled));
        driver.SelectionHighlights.OnNext(new([new TelemetryHighlightRange(1, 2)]));
        Assert.All(Actions(1), action => Assert.True(action.IsEnabled));

        controller.SpeedHeaderActions[1].Command!.Execute(null);

        Assert.True(state.Presentation.Signals.ShowSpeedAnalysisSelection);
        Assert.True(controller.SpeedHeaderActions[1].IsChecked);
        driver.Actions.ClearAnalysisSelection();
        Assert.All(Actions(1), action =>
        {
            Assert.False(action.IsEnabled);
            Assert.False(action.IsChecked);
        });
    }

    private IEnumerable<SignalRowAction> Actions(int index) =>
    [
        controller.TravelHeaderActions[index], controller.VelocityHeaderActions[index],
        controller.ImuHeaderActions[index], controller.PitchRollHeaderActions[index],
        controller.SpeedHeaderActions[index], controller.ElevationHeaderActions[index],
    ];

    public void Dispose()
    {
        subscription.Dispose();
        driver.Dispose();
    }
}
