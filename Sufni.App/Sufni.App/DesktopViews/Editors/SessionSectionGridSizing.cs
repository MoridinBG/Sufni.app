using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

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
}
