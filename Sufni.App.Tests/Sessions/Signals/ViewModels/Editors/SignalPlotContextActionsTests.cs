using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Terrain.Models;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

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
            "session-trim-from-beginning", "session-trim-until-end", "session-split-here", "session-undo-trim", "save-current-selection",
            "label-roughness", "label-steepness"];
        foreach (var row in new[] { SignalRowIds.Travel, SignalRowIds.Velocity, SignalRowIds.Imu,
                     SignalRowIds.PitchRoll, SignalRowIds.Speed, SignalRowIds.Elevation })
            Assert.Equal(expected, harness.ContextActions.SignalPlotContextMenuActionsBySignalRowId[row].Select(action => action.Id));
    }

    [Fact]
    public void LabelActions_AreOnEveryRowThatSavesTheSelection()
    {
        using var harness = new SessionPlotActionsFixture();
        foreach (var actions in harness.ContextActions.SignalPlotContextMenuActionsBySignalRowId.Values)
            Assert.Equal(2, actions.Count(action => action.Id is "label-roughness" or "label-steepness"));
        Assert.Equal(10, harness.ContextActions.SignalPlotContextMenuActionsBySignalRowId.Count);
    }

    [Theory]
    [InlineData(3, true, true)]
    [InlineData(6, true, false)]
    [InlineData(3, false, false)]
    public void LabelActions_NeedAClickInTheSelectionAndLoadedTerrain(double clickSeconds, bool loaded, bool available)
    {
        using var harness = new SessionPlotActionsFixture();
        if (loaded)
        {
            var classified = TerrainProfiles.Classified(TerrainProfiles.Window(0));
            harness.State.OnNext(harness.State.Value with
            {
                Terrain = new RecordedTerrainState.Loaded(TestSnapshots.WithCurrentTerrain(harness.State.Value.Session!,
                    TerrainOutcome.Computed).TerrainProfileKey!, classified, RecordedSessionPresentationDeriver.CreateTerrain(classified))
            });
        }
        var context = CreatePlotContext(clickSeconds) with { AnalysisRange = new TelemetryTimeRange(2, 4) };

        Assert.Equal((available, available), (harness.ContextActions.LabelRoughnessCommand.CanExecute(context),
            harness.ContextActions.LabelSteepnessCommand.CanExecute(context)));
    }

    private static TelemetryPlotContextMenuContext CreatePlotContext(double clickSeconds) => SessionPlotActionsFixture.Context(clickSeconds);
}
