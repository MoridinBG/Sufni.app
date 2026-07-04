using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using ScottPlot.Avalonia;
using Sufni.App.ExtensionHost.Contracts.Presentation;

namespace Sufni.App.Shared.Views.Plots;

internal sealed class TelemetryPlotContextMenu : IPlotMenu
{
    private static readonly string[] NormalActionOrder =
    [
        "autozoom",
        "save-current-selection",
        "analysis-range-set-start",
        "analysis-range-clear",
        "gps-mark-gps-event",
        "private-video-mark-video-event",
        "private-session-editing-trim-from-beginning",
        "private-session-editing-trim-until-end",
        "private-session-editing-split-from-here",
        "private-session-editing-undo-trim",
    ];

    private readonly IPlotMenu innerMenu;
    private readonly Func<List<ContextMenuItem>> getDefaultContextMenuItems;
    private readonly Func<Pixel, TelemetryPlotContextMenuContext?> createContext;
    private readonly Func<TelemetryPlotContextMenuContext, IReadOnlyList<TelemetryPlotContextMenuAction>> getActions;

    public TelemetryPlotContextMenu(
        SufniAvaPlot plotControl,
        Func<Pixel, TelemetryPlotContextMenuContext?> createContext,
        Func<TelemetryPlotContextMenuContext, IReadOnlyList<TelemetryPlotContextMenuAction>> getActions)
    {
        var avaPlotMenu = new AvaPlotMenu(plotControl);
        innerMenu = avaPlotMenu;
        getDefaultContextMenuItems = () => avaPlotMenu.GetDefaultContextMenuItems().ToList();
        this.createContext = createContext;
        this.getActions = getActions;
        Reset();
    }

    internal TelemetryPlotContextMenu(
        IPlotMenu innerMenu,
        IEnumerable<ContextMenuItem> defaultContextMenuItems,
        Func<Pixel, TelemetryPlotContextMenuContext?> createContext,
        Func<TelemetryPlotContextMenuContext, IReadOnlyList<TelemetryPlotContextMenuAction>> getActions)
    {
        this.innerMenu = innerMenu;
        getDefaultContextMenuItems = () => defaultContextMenuItems.ToList();
        this.createContext = createContext;
        this.getActions = getActions;
        Reset();
    }

    public List<ContextMenuItem> ContextMenuItems { get; set; } = [];

    public void Reset()
    {
        ContextMenuItems = getDefaultContextMenuItems();
    }

    public void Clear()
    {
        ContextMenuItems.Clear();
    }

    public void Add(string Label, Action<Plot> action)
    {
        ContextMenuItems.Add(new ContextMenuItem
        {
            Label = Label,
            OnInvoke = action
        });
    }

    public void AddSeparator()
    {
        ContextMenuItems.Add(new ContextMenuItem
        {
            IsSeparator = true
        });
    }

    public void ShowContextMenu(Pixel pixel)
    {
        innerMenu.ContextMenuItems.Clear();
        innerMenu.ContextMenuItems.AddRange(ContextMenuItems);

        var context = createContext(pixel);
        if (context is null)
        {
            innerMenu.ShowContextMenu(pixel);
            return;
        }

        var executableActions = getActions(context)
            .Where(action => !string.IsNullOrWhiteSpace(action.Label) &&
                             action.Command.CanExecute(context))
            .ToArray();
        var actions = CreateDisplayActions(context, executableActions);
        if (actions.Length == 0)
        {
            innerMenu.ShowContextMenu(pixel);
            return;
        }

        if (innerMenu.ContextMenuItems.Count > 0)
        {
            innerMenu.AddSeparator();
        }

        foreach (var action in actions)
        {
            innerMenu.Add(action.Label, _ =>
            {
                if (action.Command.CanExecute(context))
                {
                    action.Command.Execute(context);
                }
            });
        }

        innerMenu.ShowContextMenu(pixel);
    }

    private static TelemetryPlotContextMenuAction[] CreateDisplayActions(
        TelemetryPlotContextMenuContext context,
        IReadOnlyList<TelemetryPlotContextMenuAction> executableActions)
    {
        var actionsById = executableActions
            .GroupBy(action => action.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        if (HasPendingAnalysisBoundary(context, actionsById))
        {
            return MaterializeKnownActions(
                actionsById,
                [
                    ("analysis-range-set-end", "Analysis > End here"),
                    ("analysis-range-clear", "Analysis > Cancel"),
                ]);
        }

        if (actionsById.ContainsKey("gps-mark-telemetry-event"))
        {
            return MaterializeKnownActions(
                actionsById,
                [
                    ("gps-mark-telemetry-event", "Sync > Mark telemetry event"),
                    ("gps-cancel-alignment", "Sync > Cancel"),
                ]);
        }

        if (actionsById.ContainsKey("private-video-mark-telemetry-event"))
        {
            return MaterializeKnownActions(
                actionsById,
                [
                    ("private-video-mark-telemetry-event", "Sync > Mark telemetry event"),
                    ("private-video-cancel-alignment", "Sync > Cancel"),
                ]);
        }

        var orderedActions = MaterializeKnownActions(
            actionsById,
            NormalActionOrder.Select(id => (id, ResolveNormalLabel(id))));
        var knownIds = NormalActionOrder.ToHashSet(StringComparer.Ordinal);
        var remainingActions = executableActions
            .Where(action => !knownIds.Contains(action.Id) && !IsPendingOnlyAction(action.Id))
            .ToArray();

        return [.. orderedActions, .. remainingActions];
    }

    private static bool HasPendingAnalysisBoundary(
        TelemetryPlotContextMenuContext context,
        IReadOnlyDictionary<string, TelemetryPlotContextMenuAction> actionsById)
    {
        return context.AnalysisRange is null &&
               actionsById.ContainsKey("analysis-range-clear");
    }

    private static TelemetryPlotContextMenuAction[] MaterializeKnownActions(
        IReadOnlyDictionary<string, TelemetryPlotContextMenuAction> actionsById,
        IEnumerable<(string Id, string Label)> actionOrder)
    {
        return actionOrder
            .Where(entry => actionsById.ContainsKey(entry.Id))
            .Select(entry => WithDisplayLabel(actionsById[entry.Id], entry.Label))
            .ToArray();
    }

    private static TelemetryPlotContextMenuAction WithDisplayLabel(
        TelemetryPlotContextMenuAction action,
        string label)
    {
        return string.Equals(action.Label, label, StringComparison.Ordinal)
            ? action
            : action with { Label = label };
    }

    private static string ResolveNormalLabel(string id)
    {
        return id switch
        {
            "save-current-selection" => "Analysis > Save current selection",
            "analysis-range-set-start" => "Analysis > Start here",
            "analysis-range-clear" => "Analysis > Clear",
            "gps-mark-gps-event" => "Sync > Mark GPS event",
            "private-video-mark-video-event" => "Sync > Mark video event",
            "private-session-editing-trim-from-beginning" => "Trim > Trim from beginning",
            "private-session-editing-trim-until-end" => "Trim > Trim until end",
            "private-session-editing-split-from-here" => "Trim > Split here",
            "private-session-editing-undo-trim" => "Trim > Undo trim",
            _ => id == "autozoom" ? "Autozoom" : id,
        };
    }

    private static bool IsPendingOnlyAction(string id)
    {
        return id is "analysis-range-set-end" or
            "gps-mark-telemetry-event" or
            "gps-cancel-alignment" or
            "private-video-mark-telemetry-event" or
            "private-video-cancel-alignment";
    }
}
