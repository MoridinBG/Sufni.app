using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;

namespace Sufni.App.Shared.Views.Plots;

// ScottPlot's AvaPlotMenu renders a flat list only, so this menu builds the Avalonia
// ContextMenu itself to show grouped actions as submenus.
internal sealed class TelemetryPlotContextMenu : IPlotMenu
{
    private readonly SufniAvaPlot plotControl;
    private readonly Func<List<ContextMenuItem>> getDefaultContextMenuItems;
    private readonly Func<Pixel, TelemetryPlotContextMenuContext?> createContext;
    private readonly Func<TelemetryPlotContextMenuContext, IReadOnlyList<TelemetryPlotContextMenuAction>> getActions;

    public TelemetryPlotContextMenu(
        SufniAvaPlot plotControl,
        Func<Pixel, TelemetryPlotContextMenuContext?> createContext,
        Func<TelemetryPlotContextMenuContext, IReadOnlyList<TelemetryPlotContextMenuAction>> getActions)
    {
        this.plotControl = plotControl;
        var avaPlotMenu = new AvaPlotMenu(plotControl);
        getDefaultContextMenuItems = () => avaPlotMenu.GetDefaultContextMenuItems().ToList();
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
        var plot = plotControl.GetPlotAtPixel(pixel);
        if (plot is null)
        {
            return;
        }

        var items = ContextMenuItems
            .Select(item => item.IsSeparator ? CreateSeparator() : CreatePlotMenuItem(item, plot))
            .ToList();

        var context = createContext(pixel);
        var entries = context is null
            ? []
            : TelemetryPlotContextMenuLayout.Create(context, getActions(context));
        if (entries.Count > 0 && items.Count > 0)
        {
            items.Add(CreateSeparator());
        }

        items.AddRange(entries.Select(entry => CreateEntryMenuItem(entry, context!)));
        if (items.Count == 0)
        {
            return;
        }

        var menu = new ContextMenu
        {
            ItemsSource = items,
            PlacementRect = new Rect(pixel.X, pixel.Y, 1, 1),
        };
        menu.Open(plotControl);
    }

    // Matches the separator that ScottPlot's AvaPlotMenu renders.
    private static MenuItem CreateSeparator() => new() { Header = "-" };

    private static MenuItem CreatePlotMenuItem(ContextMenuItem item, Plot plot)
    {
        var menuItem = new MenuItem { Header = item.Label };
        menuItem.Click += (_, _) => item.OnInvoke(plot);
        return menuItem;
    }

    private static MenuItem CreateEntryMenuItem(
        TelemetryPlotContextMenuEntry entry,
        TelemetryPlotContextMenuContext context)
    {
        return entry switch
        {
            TelemetryPlotContextMenuEntry.Item item => CreateActionMenuItem(item, context),
            TelemetryPlotContextMenuEntry.Submenu submenu => new MenuItem
            {
                Header = submenu.Header,
                ItemsSource = submenu.Items
                    .Select(item => CreateActionMenuItem(item, context))
                    .ToList(),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(entry)),
        };
    }

    private static MenuItem CreateActionMenuItem(
        TelemetryPlotContextMenuEntry.Item item,
        TelemetryPlotContextMenuContext context)
    {
        var action = item.Action;
        var menuItem = new MenuItem { Header = action.Label };
        ToolTip.SetTip(menuItem, item.ToolTip);
        menuItem.Click += (_, _) =>
        {
            if (action.Command.CanExecute(context))
            {
                action.Command.Execute(context);
            }
        };
        return menuItem;
    }
}
