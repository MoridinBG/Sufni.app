using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.Telemetry;

using Sufni.App.Sessions.Signals.ViewModels.Editors;
namespace Sufni.App.Tests.Sessions.Signals.ViewModels.Editors;

public class SignalRowActionsControllerTests
{
    private readonly SignalRowActionState state = new();
    private readonly SignalRowActionsController controller;

    public SignalRowActionsControllerTests()
    {
        controller = CreateController(state);
    }

    [Fact]
    public void Constructor_CreatesHeaderActionPairs()
    {
        Assert.Equal(
            ["travel_airtime", "travel_analysis_selection"],
            controller.TravelHeaderActions.Select(action => action.Id));
        Assert.Equal(
            ["elevation_airtime", "elevation_analysis_selection"],
            controller.ElevationHeaderActions.Select(action => action.Id));
        Assert.All(
            controller.TravelHeaderActions,
            action => Assert.Equal(SignalRowActionKind.Toggle, action.Kind));
    }

    [Fact]
    public void Constructor_InitialActionStates_MirrorContextDefaults()
    {
        // Travel airtime defaults to visible; the other airtime rows start hidden.
        Assert.True(controller.TravelHeaderActions[0].IsChecked);
        Assert.False(controller.VelocityHeaderActions[0].IsChecked);
        // Statistics-selection toggles start disabled until a selection exists.
        Assert.False(controller.TravelHeaderActions[1].IsEnabled);
        Assert.False(controller.SpeedHeaderActions[1].IsEnabled);
    }

    [Fact]
    public void AirtimeActionCommand_TogglesContextFlag_AndSyncsCheckedState()
    {
        var airtimeAction = controller.TravelHeaderActions[0];

        airtimeAction.Command!.Execute(null);

        Assert.False(state.ShowAirtime);
        Assert.False(airtimeAction.IsChecked);

        airtimeAction.Command.Execute(null);

        Assert.True(state.ShowAirtime);
        Assert.True(airtimeAction.IsChecked);
    }

    [Fact]
    public void ExternalShowFlagChange_DoesNotUpdateAirtimeActionCheckedState()
    {
        state.ShowVelocityAirtime = true;

        Assert.False(controller.VelocityHeaderActions[0].IsChecked);
    }

    [Fact]
    public void AnalysisSelectionActionCommand_TogglesContextFlag_AndSyncsCheckedState()
    {
        var selectionAction = controller.TravelHeaderActions[1];

        selectionAction.Command!.Execute(null);

        Assert.True(state.ShowAnalysisSelection);
        Assert.True(selectionAction.IsChecked);
    }

    [Fact]
    public void RefreshAnalysisSelectionActionStates_EnablesActions_WhileSelectionExists()
    {
        state.AnalysisSelectionHighlightRanges = [new TelemetryHighlightRange(1.0, 2.0)];

        controller.RefreshAnalysisSelectionActionStates();

        Assert.All(
            AllAnalysisSelectionActions(),
            action => Assert.True(action.IsEnabled));

        state.AnalysisSelectionHighlightRanges = [];
        controller.RefreshAnalysisSelectionActionStates();

        Assert.All(
            AllAnalysisSelectionActions(),
            action => Assert.False(action.IsEnabled));
    }

    [Fact]
    public void ClearAnalysisSelectionToggles_UnchecksEveryToggle_AndResetsContextFlags()
    {
        state.ShowAnalysisSelection = true;
        state.ShowVelocityAnalysisSelection = true;
        state.ShowElevationAnalysisSelection = true;

        controller.ClearAnalysisSelectionToggles();

        Assert.False(state.ShowAnalysisSelection);
        Assert.False(state.ShowVelocityAnalysisSelection);
        Assert.False(state.ShowImuAnalysisSelection);
        Assert.False(state.ShowPitchRollAnalysisSelection);
        Assert.False(state.ShowSpeedAnalysisSelection);
        Assert.False(state.ShowElevationAnalysisSelection);
        Assert.All(
            AllAnalysisSelectionActions(),
            action => Assert.False(action.IsChecked));
    }

    private IEnumerable<SignalRowAction> AllAnalysisSelectionActions()
    {
        return
        [
            controller.TravelHeaderActions[1],
            controller.VelocityHeaderActions[1],
            controller.ImuHeaderActions[1],
            controller.PitchRollHeaderActions[1],
            controller.SpeedHeaderActions[1],
            controller.ElevationHeaderActions[1],
        ];
    }

    private static SignalRowActionsController CreateController(SignalRowActionState state)
    {
        return new SignalRowActionsController(
            () => state.HasAnalysisSelection,
            () => state.ShowAirtime,
            value => state.ShowAirtime = value,
            () => state.ShowVelocityAirtime,
            value => state.ShowVelocityAirtime = value,
            () => state.ShowImuAirtime,
            value => state.ShowImuAirtime = value,
            () => state.ShowPitchRollAirtime,
            value => state.ShowPitchRollAirtime = value,
            () => state.ShowSpeedAirtime,
            value => state.ShowSpeedAirtime = value,
            () => state.ShowElevationAirtime,
            value => state.ShowElevationAirtime = value,
            () => state.ShowAnalysisSelection,
            value => state.ShowAnalysisSelection = value,
            () => state.ShowVelocityAnalysisSelection,
            value => state.ShowVelocityAnalysisSelection = value,
            () => state.ShowImuAnalysisSelection,
            value => state.ShowImuAnalysisSelection = value,
            () => state.ShowPitchRollAnalysisSelection,
            value => state.ShowPitchRollAnalysisSelection = value,
            () => state.ShowSpeedAnalysisSelection,
            value => state.ShowSpeedAnalysisSelection = value,
            () => state.ShowElevationAnalysisSelection,
            value => state.ShowElevationAnalysisSelection = value);
    }

    private sealed class SignalRowActionState
    {
        public IReadOnlyList<TelemetryHighlightRange> AnalysisSelectionHighlightRanges { get; set; } = [];
        public bool HasAnalysisSelection => AnalysisSelectionHighlightRanges.Count > 0;
        public bool ShowAirtime { get; set; } = true;
        public bool ShowVelocityAirtime { get; set; }
        public bool ShowImuAirtime { get; set; }
        public bool ShowPitchRollAirtime { get; set; }
        public bool ShowSpeedAirtime { get; set; }
        public bool ShowElevationAirtime { get; set; }
        public bool ShowAnalysisSelection { get; set; }
        public bool ShowVelocityAnalysisSelection { get; set; }
        public bool ShowImuAnalysisSelection { get; set; }
        public bool ShowPitchRollAnalysisSelection { get; set; }
        public bool ShowSpeedAnalysisSelection { get; set; }
        public bool ShowElevationAnalysisSelection { get; set; }
    }
}
