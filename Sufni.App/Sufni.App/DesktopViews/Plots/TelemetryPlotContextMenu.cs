using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using ScottPlot.Avalonia;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Plots;

namespace Sufni.App.DesktopViews.Plots;

internal sealed class TelemetryPlotContextMenu : IPlotMenu
{
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

        var actions = getActions(context)
            .Where(action => !string.IsNullOrWhiteSpace(action.Label) &&
                             action.Command.CanExecute(context))
            .ToArray();
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
}
