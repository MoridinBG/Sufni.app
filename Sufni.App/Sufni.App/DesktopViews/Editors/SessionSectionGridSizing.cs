using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Sufni.App.Models;

namespace Sufni.App.DesktopViews.Editors;

public static class SessionSectionGridSizing
{
    public static void AttachRowReset(GridSplitter? splitter, Grid grid, params (int Row, GridLength Height)[] rows)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (splitter is null)
        {
            return;
        }

        // GridSplitter can handle input during drag gestures, so listen even when the double-tap is already handled.
        splitter.AddHandler<TappedEventArgs>(
            InputElement.DoubleTappedEvent,
            (_, args) =>
            {
                ResetRows(grid, rows);
                args.Handled = true;
            },
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    public static void AttachColumnReset(GridSplitter? splitter, Grid grid, params (int Column, GridLength Width)[] columns)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (splitter is null)
        {
            return;
        }

        // GridSplitter can handle input during drag gestures, so listen even when the double-tap is already handled.
        splitter.AddHandler<TappedEventArgs>(
            InputElement.DoubleTappedEvent,
            (_, args) =>
            {
                ResetColumns(grid, columns);
                args.Handled = true;
            },
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    public static void ResetRows(Grid grid, params (int Row, GridLength Height)[] rows)
    {
        ArgumentNullException.ThrowIfNull(grid);

        foreach (var (row, height) in rows)
        {
            if (row >= 0 && row < grid.RowDefinitions.Count)
            {
                grid.RowDefinitions[row].Height = height;
            }
        }
    }

    public static void ResetColumns(Grid grid, params (int Column, GridLength Width)[] columns)
    {
        ArgumentNullException.ThrowIfNull(grid);

        foreach (var (column, width) in columns)
        {
            if (column >= 0 && column < grid.ColumnDefinitions.Count)
            {
                grid.ColumnDefinitions[column].Width = width;
            }
        }
    }

    public static void AttachCommit(GridSplitter? splitter, Action commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (splitter is null)
        {
            return;
        }

        void ScheduleCommit()
        {
            Dispatcher.UIThread.Post(commit, DispatcherPriority.Background);
        }

        splitter.AddHandler<PointerReleasedEventArgs>(
            InputElement.PointerReleasedEvent,
            (_, _) => ScheduleCommit(),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        splitter.PointerCaptureLost += (_, _) => ScheduleCommit();
    }

    public static SessionPaneGroupPreferences CaptureRatios(params (string PaneId, double Size)[] panes)
    {
        var visiblePanes = panes
            .Where(static pane => !string.IsNullOrWhiteSpace(pane.PaneId) &&
                                  double.IsFinite(pane.Size) &&
                                  pane.Size > 0)
            .ToArray();
        var total = visiblePanes.Sum(static pane => pane.Size);
        if (total <= 0)
        {
            return new SessionPaneGroupPreferences();
        }

        return new SessionPaneGroupPreferences(visiblePanes
            .Select(pane => new SessionPaneSizePreference(pane.PaneId, pane.Size / total))
            .ToArray());
    }

    public static bool TryApplyRowRatios(
        Grid grid,
        SessionPaneGroupPreferences? preferences,
        IReadOnlyList<(int Row, string PaneId)> panes)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (!TryGetRatios(preferences, panes.Select(static pane => pane.PaneId).ToArray(), out var ratios))
        {
            return false;
        }

        for (var i = 0; i < panes.Count; i++)
        {
            var row = panes[i].Row;
            if (row >= 0 && row < grid.RowDefinitions.Count)
            {
                grid.RowDefinitions[row].Height = new GridLength(ratios[i], GridUnitType.Star);
            }
        }

        return true;
    }

    public static bool TryApplyColumnRatios(
        Grid grid,
        SessionPaneGroupPreferences? preferences,
        IReadOnlyList<(int Column, string PaneId)> panes)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (!TryGetRatios(preferences, panes.Select(static pane => pane.PaneId).ToArray(), out var ratios))
        {
            return false;
        }

        for (var i = 0; i < panes.Count; i++)
        {
            var column = panes[i].Column;
            if (column >= 0 && column < grid.ColumnDefinitions.Count)
            {
                grid.ColumnDefinitions[column].Width = new GridLength(ratios[i], GridUnitType.Star);
            }
        }

        return true;
    }

    private static bool TryGetRatios(
        SessionPaneGroupPreferences? preferences,
        IReadOnlyList<string> paneIds,
        out IReadOnlyList<double> ratios)
    {
        ratios = [];
        if (preferences is null || !preferences.TryGetRatios(paneIds, out var storedRatios))
        {
            return false;
        }

        var total = storedRatios.Sum();
        if (!double.IsFinite(total) || total <= 0)
        {
            return false;
        }

        ratios = storedRatios.Select(ratio => ratio / total).ToArray();
        return true;
    }
}
