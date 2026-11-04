using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Signals.ViewModels.Editors;

public class SignalPlotContextActionsTests
{
    [Fact]
    public void SaveCurrentSelection_IsUnavailableOutsideTheAnalysisRange()
    {
        using var harness = new SessionPlotActionsFixture();
        var context = CreatePlotContext(6) with { AnalysisRange = new TelemetryTimeRange(2, 4) };
        Assert.False(harness.ContextActions.SaveSelectionAsSegmentCommand.CanExecute(context));
        Assert.True(harness.ContextActions.SaveSelectionAsSegmentCommand.CanExecute(context with { ClickSeconds = 3 }));
    }

    [Fact]
    public void EveryRow_HasAutozoomThenTheEditorActions()
    {
        using var harness = new SessionPlotActionsFixture();
        string[] expected = ["zoom-selection", "analysis-range-set-start", "analysis-range-set-end", "analysis-range-clear",
            "gps-mark-gps-event", "gps-mark-telemetry-event", "gps-cancel-alignment",
            "video-mark-video-event", "video-mark-telemetry-event", "video-cancel-alignment",
            "session-trim-from-beginning", "session-trim-until-end", "session-split-here", "session-undo-trim", "save-current-selection"];
        foreach (var row in new[] { SignalRowIds.Travel, SignalRowIds.Velocity, SignalRowIds.Imu,
                     SignalRowIds.PitchRoll, SignalRowIds.Speed, SignalRowIds.Elevation })
            Assert.Equal(expected, harness.ContextActions.SignalPlotContextMenuActionsBySignalRowId[row].Select(action => action.Id));
    }

    private static TelemetryPlotContextMenuContext CreatePlotContext(double clickSeconds) => SessionPlotActionsFixture.Context(clickSeconds);
}
