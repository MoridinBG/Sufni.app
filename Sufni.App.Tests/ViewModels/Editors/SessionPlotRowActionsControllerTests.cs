using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.Telemetry;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Graph.ViewModels.Editors;
namespace Sufni.App.Tests.ViewModels.Editors;

public class SessionPlotRowActionsControllerTests
{
    private readonly RecordedSessionContext context = new();
    private readonly SessionPlotRowActionsController controller;

    public SessionPlotRowActionsControllerTests()
    {
        controller = new SessionPlotRowActionsController(context);
    }

    [Fact]
    public void Constructor_PublishesHeaderActionPairsToContext()
    {
        Assert.Same(controller.TravelHeaderActions, context.TravelHeaderActions);
        Assert.Same(controller.VelocityHeaderActions, context.VelocityHeaderActions);
        Assert.Same(controller.ImuHeaderActions, context.ImuHeaderActions);
        Assert.Same(controller.PitchRollHeaderActions, context.PitchRollHeaderActions);
        Assert.Same(controller.SpeedHeaderActions, context.SpeedHeaderActions);
        Assert.Same(controller.ElevationHeaderActions, context.ElevationHeaderActions);
        Assert.Equal(
            ["travel_airtime", "travel_statistics_selection"],
            controller.TravelHeaderActions.Select(action => action.Id));
        Assert.Equal(
            ["elevation_airtime", "elevation_statistics_selection"],
            controller.ElevationHeaderActions.Select(action => action.Id));
        Assert.All(
            controller.TravelHeaderActions,
            action => Assert.Equal(TelemetryPlotRowActionKind.Toggle, action.Kind));
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
    public void ContextShowFlagChange_UpdatesAirtimeActionCheckedState()
    {
        context.ShowVelocityAirtime = true;

        Assert.True(controller.VelocityHeaderActions[0].IsChecked);
    }

    [Fact]
    public void StatisticsSelectionActionCommand_TogglesContextFlag_AndSyncsCheckedState()
    {
        var selectionAction = controller.TravelHeaderActions[1];

        selectionAction.Command!.Execute(null);

        Assert.True(context.ShowStatisticsSelection);
        Assert.True(selectionAction.IsChecked);
    }

    [Fact]
    public void RefreshStatisticsSelectionActionStates_EnablesActions_WhileSelectionExists()
    {
        context.StatisticsSelectionHighlightRanges = [new TelemetryHighlightRange(1.0, 2.0)];

        controller.RefreshStatisticsSelectionActionStates();

        Assert.All(
            AllStatisticsSelectionActions(),
            action => Assert.True(action.IsEnabled));

        context.StatisticsSelectionHighlightRanges = [];
        controller.RefreshStatisticsSelectionActionStates();

        Assert.All(
            AllStatisticsSelectionActions(),
            action => Assert.False(action.IsEnabled));
    }

    [Fact]
    public void ClearStatisticsSelectionToggles_UnchecksEveryToggle_AndResetsContextFlags()
    {
        context.ShowStatisticsSelection = true;
        context.ShowVelocityStatisticsSelection = true;
        context.ShowElevationStatisticsSelection = true;

        controller.ClearStatisticsSelectionToggles();

        Assert.False(context.ShowStatisticsSelection);
        Assert.False(context.ShowVelocityStatisticsSelection);
        Assert.False(context.ShowImuStatisticsSelection);
        Assert.False(context.ShowPitchRollStatisticsSelection);
        Assert.False(context.ShowSpeedStatisticsSelection);
        Assert.False(context.ShowElevationStatisticsSelection);
        Assert.All(
            AllStatisticsSelectionActions(),
            action => Assert.False(action.IsChecked));
    }

    private IEnumerable<TelemetryPlotRowAction> AllStatisticsSelectionActions()
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
}
