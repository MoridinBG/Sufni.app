using CommunityToolkit.Mvvm.Input;
using Sufni.App.Shared.Views.Plots;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Shared.Views.Plots;

public class TelemetryPlotContextMenuLayoutTests
{
    private static readonly TelemetryPlotContextMenuContext ContextInsideRange =
        new("travel", 3, 10, new TelemetryTimeRange(2, 4));

    [Fact]
    public void Create_GroupsNormalActionsIntoOrderedSubmenus()
    {
        var actions = CreateActions(
            "session-split-here", "gps-mark-gps-event", "analysis-range-clear", "zoom-selection",
            "label-steepness", "label-roughness", "session-trim-from-beginning", "save-current-selection",
            "video-mark-video-event", "analysis-range-set-start");

        var entries = TelemetryPlotContextMenuLayout.Create(ContextInsideRange, actions);

        Assert.Collection(
            entries,
            entry => Assert.Equal("zoom-selection", Assert.IsType<TelemetryPlotContextMenuEntry.Item>(entry).Action.Id),
            entry => AssertSubmenu(entry, "save-current-selection", "analysis-range-set-start", "analysis-range-clear"),
            entry => AssertSubmenu(entry, "label-roughness", "label-steepness"),
            entry => AssertSubmenu(entry, "gps-mark-gps-event", "video-mark-video-event"),
            entry => AssertSubmenu(entry, "session-trim-from-beginning", "session-split-here"));
    }

    [Fact]
    public void Create_OmitsSubmenus_WhoseActionsCannotExecute()
    {
        var actions = CreateActions("session-trim-from-beginning", "session-undo-trim")
            .Append(CreateAction("save-current-selection", canExecute: false))
            .ToArray();

        var entries = TelemetryPlotContextMenuLayout.Create(ContextInsideRange, actions);

        var entry = Assert.Single(entries);
        AssertSubmenu(entry, "session-trim-from-beginning", "session-undo-trim");
    }

    [Fact]
    public void Create_KeepsPendingAnalysisBoundaryActionsFlat()
    {
        var context = ContextInsideRange with { AnalysisRange = null };
        var actions = CreateActions(
            "zoom-selection", "analysis-range-set-start", "analysis-range-set-end", "analysis-range-clear",
            "session-split-here");

        var entries = TelemetryPlotContextMenuLayout.Create(context, actions);

        Assert.Equal(
            ["analysis-range-set-end", "analysis-range-clear"],
            entries.Select(entry => Assert.IsType<TelemetryPlotContextMenuEntry.Item>(entry).Action.Id));
    }

    [Fact]
    public void Create_KeepsPendingAlignmentActionsFlat()
    {
        var actions = CreateActions(
            "zoom-selection", "gps-mark-telemetry-event", "gps-cancel-alignment", "session-split-here");

        var entries = TelemetryPlotContextMenuLayout.Create(ContextInsideRange, actions);

        Assert.Equal(
            ["gps-mark-telemetry-event", "gps-cancel-alignment"],
            entries.Select(entry => Assert.IsType<TelemetryPlotContextMenuEntry.Item>(entry).Action.Id));
    }

    private static void AssertSubmenu(TelemetryPlotContextMenuEntry entry, params string[] expectedIds)
    {
        var submenu = Assert.IsType<TelemetryPlotContextMenuEntry.Submenu>(entry);
        Assert.Equal(expectedIds, submenu.Items.Select(item => item.Action.Id));
    }

    private static TelemetryPlotContextMenuAction[] CreateActions(params string[] ids) =>
        ids.Select(id => CreateAction(id)).ToArray();

    private static TelemetryPlotContextMenuAction CreateAction(string id, bool canExecute = true) =>
        new(id, id, new RelayCommand<TelemetryPlotContextMenuContext?>(_ => { }, _ => canExecute));
}
