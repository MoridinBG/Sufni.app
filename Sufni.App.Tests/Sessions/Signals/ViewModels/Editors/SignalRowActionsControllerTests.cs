using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.Telemetry;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
namespace Sufni.App.Tests.Sessions.Signals.ViewModels.Editors;

public class SignalRowActionsControllerTests
{
    private readonly RecordedSessionContext context = new();
    private readonly SignalRowActionsController controller;

    public SignalRowActionsControllerTests()
    {
        controller = CreateController(context);
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

        Assert.False(context.ShowAirtime);
        Assert.False(airtimeAction.IsChecked);

        airtimeAction.Command.Execute(null);

        Assert.True(context.ShowAirtime);
        Assert.True(airtimeAction.IsChecked);
    }

    [Fact]
    public void ExternalShowFlagChange_DoesNotUpdateAirtimeActionCheckedState()
    {
        context.ShowVelocityAirtime = true;

        Assert.False(controller.VelocityHeaderActions[0].IsChecked);
    }

    [Fact]
    public void AnalysisSelectionActionCommand_TogglesContextFlag_AndSyncsCheckedState()
    {
        var selectionAction = controller.TravelHeaderActions[1];

        selectionAction.Command!.Execute(null);

        Assert.True(context.ShowAnalysisSelection);
        Assert.True(selectionAction.IsChecked);
    }

    [Fact]
    public void RefreshAnalysisSelectionActionStates_EnablesActions_WhileSelectionExists()
    {
        context.AnalysisSelectionHighlightRanges = [new TelemetryHighlightRange(1.0, 2.0)];

        controller.RefreshAnalysisSelectionActionStates();

        Assert.All(
            AllAnalysisSelectionActions(),
            action => Assert.True(action.IsEnabled));

        context.AnalysisSelectionHighlightRanges = [];
        controller.RefreshAnalysisSelectionActionStates();

        Assert.All(
            AllAnalysisSelectionActions(),
            action => Assert.False(action.IsEnabled));
    }

    [Fact]
    public void ClearAnalysisSelectionToggles_UnchecksEveryToggle_AndResetsContextFlags()
    {
        context.ShowAnalysisSelection = true;
        context.ShowVelocityAnalysisSelection = true;
        context.ShowElevationAnalysisSelection = true;

        controller.ClearAnalysisSelectionToggles();

        Assert.False(context.ShowAnalysisSelection);
        Assert.False(context.ShowVelocityAnalysisSelection);
        Assert.False(context.ShowImuAnalysisSelection);
        Assert.False(context.ShowPitchRollAnalysisSelection);
        Assert.False(context.ShowSpeedAnalysisSelection);
        Assert.False(context.ShowElevationAnalysisSelection);
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

    private static SignalRowActionsController CreateController(RecordedSessionContext context)
    {
        return new SignalRowActionsController(
            () => context.HasAnalysisSelection,
            () => context.ShowAirtime,
            value => context.ShowAirtime = value,
            () => context.ShowVelocityAirtime,
            value => context.ShowVelocityAirtime = value,
            () => context.ShowImuAirtime,
            value => context.ShowImuAirtime = value,
            () => context.ShowPitchRollAirtime,
            value => context.ShowPitchRollAirtime = value,
            () => context.ShowSpeedAirtime,
            value => context.ShowSpeedAirtime = value,
            () => context.ShowElevationAirtime,
            value => context.ShowElevationAirtime = value,
            () => context.ShowAnalysisSelection,
            value => context.ShowAnalysisSelection = value,
            () => context.ShowVelocityAnalysisSelection,
            value => context.ShowVelocityAnalysisSelection = value,
            () => context.ShowImuAnalysisSelection,
            value => context.ShowImuAnalysisSelection = value,
            () => context.ShowPitchRollAnalysisSelection,
            value => context.ShowPitchRollAnalysisSelection = value,
            () => context.ShowSpeedAnalysisSelection,
            value => context.ShowSpeedAnalysisSelection = value,
            () => context.ShowElevationAnalysisSelection,
            value => context.ShowElevationAnalysisSelection = value);
    }
}
