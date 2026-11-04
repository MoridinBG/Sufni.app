using Avalonia.Headless.XUnit;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Signals.ViewModels.Editors;

[Collection("Ui")]
public class RecordedSessionSignalsWorkspaceViewModelTests
{
    [AvaloniaFact]
    public async Task TravelAirtime_KeepsTheUserToggleWhenRecordingDataArrives()
    {
        using var harness = new SessionDetailHarness();
        var telemetry = TestTelemetryData.CreateMinimal() with { Airtimes = [new Airtime { Start = 0.5, End = 1 }] };
        var editor = harness.CreateRecordedEditor(loadResult: harness.CreateLoadedResult(telemetry));
        var workspace = editor.SignalsWorkspace;
        var toggle = workspace.TravelHeaderActions.Single(action => action.Id == "travel_airtime");
        Assert.True(workspace.ShowAirtime);
        Assert.False(toggle.IsVisible);

        toggle.Command!.Execute(null);
        await harness.LoadAsync(editor);

        Assert.False(workspace.ShowAirtime);
        Assert.False(toggle.IsChecked);
        Assert.True(toggle.IsVisible);
        toggle.Command.Execute(null);
        Assert.True(workspace.ShowAirtime);
        Assert.True(toggle.IsChecked);
        await harness.CloseAsync(editor);
    }
}
