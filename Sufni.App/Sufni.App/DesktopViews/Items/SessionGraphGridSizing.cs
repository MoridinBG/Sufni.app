using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Sufni.App.Views.Controls;

namespace Sufni.App.DesktopViews.Items;

public static class SessionGraphGridSizing
{
    public static void AttachEqualHeightReset(Grid graphGrid, params GridSplitter?[] splitters)
    {
        ArgumentNullException.ThrowIfNull(graphGrid);

        foreach (var splitter in splitters)
        {
            if (splitter is null)
            {
                continue;
            }

            splitter.DoubleTapped += (_, args) =>
            {
                ResetVisiblePlotRows(graphGrid);
                args.Handled = true;
            };
        }
    }

    public static void ResetVisiblePlotRows(Grid graphGrid)
    {
        ArgumentNullException.ThrowIfNull(graphGrid);

        var visibleRows = new HashSet<int>();
        foreach (var child in graphGrid.Children)
        {
            if (child is PlaceholderOverlayContainer { IsVisible: true } container)
            {
                visibleRows.Add(Grid.GetRow(container));
            }
        }

        for (var rowIndex = 0; rowIndex < graphGrid.RowDefinitions.Count; rowIndex += 2)
        {
            graphGrid.RowDefinitions[rowIndex].Height = visibleRows.Contains(rowIndex)
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0, GridUnitType.Pixel);
        }
    }
}
