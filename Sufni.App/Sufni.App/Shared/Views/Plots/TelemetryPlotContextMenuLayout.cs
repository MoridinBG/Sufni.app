using System;
using System.Collections.Generic;
using System.Linq;

namespace Sufni.App.Shared.Views.Plots;

internal abstract record TelemetryPlotContextMenuEntry
{
    private TelemetryPlotContextMenuEntry()
    {
    }

    public sealed record Item(TelemetryPlotContextMenuAction Action, string? ToolTip) : TelemetryPlotContextMenuEntry;

    public sealed record Submenu(string Header, IReadOnlyList<Item> Items) : TelemetryPlotContextMenuEntry;
}

internal static class TelemetryPlotContextMenuLayout
{
    // Submenus appear in the order of their first action here.
    private static readonly (string Id, string? Group, string Label, string ToolTip)[] NormalActions =
    [
        ("zoom-selection", null, "Zoom selection",
            "Zooms all linked plots to the analysis range, with a small margin on each side."),
        ("save-current-selection", "Analysis", "Save current selection",
            "Saves the analysis range as a named segment. You type the name next."),
        ("analysis-range-set-start", "Analysis", "Start here",
            "Starts a new analysis range at this point. Then open this menu again and choose End here."),
        ("analysis-range-clear", "Analysis", "Clear",
            "Removes the analysis range from all plots."),
        ("label-current-selection", "Terrain", "Label current selection",
            "Labels the analysis range with a terrain class. You choose the class next."),
        ("gps-mark-gps-event", "Sync", "Mark GPS event",
            "Marks an event you see in the GPS data at this point. Then mark the same event in the suspension data to bring the GPS track into sync."),
        ("video-mark-video-event", "Sync", "Mark video event",
            "Marks an event you see in the video at this point. Then mark the same event in the suspension data to bring the video into sync."),
        ("session-trim-from-beginning", "Trim", "Trim from beginning",
            "Removes everything before this point from the session. Undo trim restores it."),
        ("session-trim-until-end", "Trim", "Trim until end",
            "Removes everything after this point from the session. Undo trim restores it."),
        ("session-split-here", "Trim", "Split here",
            "Splits the session into two sessions at this point, named (1) and (2)."),
        ("session-undo-trim", "Trim", "Undo trim",
            "Restores the part of the session that the last trim removed."),
    ];

    public static IReadOnlyList<TelemetryPlotContextMenuEntry> Create(
        TelemetryPlotContextMenuContext context,
        IReadOnlyList<TelemetryPlotContextMenuAction> actions)
    {
        var executableActions = actions
            .Where(action => !string.IsNullOrWhiteSpace(action.Label) &&
                             action.Command.CanExecute(context))
            .ToArray();
        var actionsById = executableActions
            .GroupBy(action => action.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        // A pending boundary or alignment shows only its two follow-up actions, so they stay flat.
        if (HasPendingAnalysisBoundary(context, actionsById))
        {
            return CreatePendingItems(
                actionsById,
                [
                    ("analysis-range-set-end", "Analysis > End here",
                        "Ends the analysis range at this point."),
                    ("analysis-range-clear", "Analysis > Cancel",
                        "Discards the analysis start point you set."),
                ]);
        }

        if (actionsById.ContainsKey("gps-mark-telemetry-event"))
        {
            return CreatePendingItems(
                actionsById,
                [
                    ("gps-mark-telemetry-event", "Sync > Mark telemetry event",
                        "Marks the same event in the suspension data. The GPS track shifts by the time between the two marks."),
                    ("gps-cancel-alignment", "Sync > Cancel",
                        "Discards the GPS event mark you set."),
                ]);
        }

        if (actionsById.ContainsKey("video-mark-telemetry-event"))
        {
            return CreatePendingItems(
                actionsById,
                [
                    ("video-mark-telemetry-event", "Sync > Mark telemetry event",
                        "Marks the same event in the suspension data. The video shifts by the time between the two marks."),
                    ("video-cancel-alignment", "Sync > Cancel",
                        "Discards the video event mark you set."),
                ]);
        }

        return CreateNormalEntries(actionsById, executableActions);
    }

    private static List<TelemetryPlotContextMenuEntry> CreateNormalEntries(
        IReadOnlyDictionary<string, TelemetryPlotContextMenuAction> actionsById,
        IReadOnlyList<TelemetryPlotContextMenuAction> executableActions)
    {
        var entries = new List<TelemetryPlotContextMenuEntry>();
        var submenuIndexByGroup = new Dictionary<string, int>(StringComparer.Ordinal);
        var submenuItems = new List<List<TelemetryPlotContextMenuEntry.Item>>();

        foreach (var (id, group, label, toolTip) in NormalActions)
        {
            if (!actionsById.TryGetValue(id, out var action))
            {
                continue;
            }

            var item = new TelemetryPlotContextMenuEntry.Item(WithDisplayLabel(action, label), toolTip);
            if (group is null)
            {
                entries.Add(item);
                continue;
            }

            if (!submenuIndexByGroup.TryGetValue(group, out var submenuIndex))
            {
                submenuIndex = submenuItems.Count;
                submenuIndexByGroup.Add(group, submenuIndex);
                submenuItems.Add([]);
                entries.Add(new TelemetryPlotContextMenuEntry.Submenu(group, submenuItems[submenuIndex]));
            }

            submenuItems[submenuIndex].Add(item);
        }

        var knownIds = NormalActions.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        entries.AddRange(executableActions
            .Where(action => !knownIds.Contains(action.Id) && !IsPendingOnlyAction(action.Id))
            .Select(action => new TelemetryPlotContextMenuEntry.Item(action, null)));

        return entries;
    }

    private static bool HasPendingAnalysisBoundary(
        TelemetryPlotContextMenuContext context,
        IReadOnlyDictionary<string, TelemetryPlotContextMenuAction> actionsById)
    {
        return context.AnalysisRange is null &&
               actionsById.ContainsKey("analysis-range-clear");
    }

    private static TelemetryPlotContextMenuEntry[] CreatePendingItems(
        IReadOnlyDictionary<string, TelemetryPlotContextMenuAction> actionsById,
        IEnumerable<(string Id, string Label, string ToolTip)> actionOrder)
    {
        return actionOrder
            .Where(entry => actionsById.ContainsKey(entry.Id))
            .Select(entry => new TelemetryPlotContextMenuEntry.Item(
                WithDisplayLabel(actionsById[entry.Id], entry.Label),
                entry.ToolTip))
            .ToArray<TelemetryPlotContextMenuEntry>();
    }

    private static TelemetryPlotContextMenuAction WithDisplayLabel(
        TelemetryPlotContextMenuAction action,
        string label)
    {
        return string.Equals(action.Label, label, StringComparison.Ordinal)
            ? action
            : action with { Label = label };
    }

    private static bool IsPendingOnlyAction(string id)
    {
        return id is "analysis-range-set-end" or
            "gps-mark-telemetry-event" or
            "gps-cancel-alignment" or
            "video-mark-telemetry-event" or
            "video-cancel-alignment";
    }
}
