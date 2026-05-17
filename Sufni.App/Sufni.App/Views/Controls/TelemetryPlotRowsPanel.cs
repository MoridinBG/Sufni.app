using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.Views.Controls;

internal sealed class TelemetryPlotRowsPanel : Panel
{
    internal const double BaseRowDividerHeight = 6;

    internal double? ViewportHeightOverride { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visibleRows = GetVisibleBaseRows();
        var preferredRowsHeight = 0.0;
        var autoGrowRows = new List<TelemetryPlotRow>();

        foreach (var row in visibleRows)
        {
            preferredRowsHeight += row.ManualGroupHeight ?? row.GetPreferredGroupHeight();
            if (row.ManualGroupHeight is null && row.IsExpanded && row.GetVisiblePlotSlotCount() > 0)
            {
                autoGrowRows.Add(row);
            }
        }

        var dividerHeight = GetDividerTotalHeight(visibleRows.Count);
        var preferredTotal = preferredRowsHeight + dividerHeight;
        var viewportHeight = ViewportHeightOverride;
        var extra = viewportHeight is { } finiteHeight
            ? Math.Max(0, finiteHeight - preferredTotal)
            : 0;
        var extraPerAutoRow = autoGrowRows.Count > 0 ? extra / autoGrowRows.Count : 0;
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;

        foreach (var row in GetBaseRows())
        {
            if (!row.ReservesLayout)
            {
                row.ApplyAllocatedGroupHeight(0);
                row.Measure(new Size(width, 0));
                continue;
            }

            var targetHeight = row.ManualGroupHeight ?? row.GetPreferredGroupHeight();
            if (autoGrowRows.Contains(row))
            {
                targetHeight += extraPerAutoRow;
            }

            var actualHeight = row.ApplyAllocatedGroupHeight(targetHeight);
            row.Measure(new Size(width, actualHeight));
        }

        foreach (var divider in Children.OfType<TelemetryBaseRowDivider>())
        {
            divider.Measure(new Size(width, BaseRowDividerHeight));
        }

        var desiredHeight = viewportHeight is { } finite
            ? Math.Max(preferredTotal, finite)
            : preferredTotal;
        var desiredWidth = Children
            .Where(child => child.IsVisible)
            .Select(child => child.DesiredSize.Width)
            .DefaultIfEmpty(width)
            .Max();

        return new Size(desiredWidth, desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visibleRows = GetVisibleBaseRows();
        var visibleRowSet = visibleRows.ToHashSet();
        var dividerByTarget = Children
            .OfType<TelemetryBaseRowDivider>()
            .Where(divider => divider.TargetRow is not null)
            .ToDictionary(divider => divider.TargetRow!);
        var y = 0.0;

        foreach (var row in GetBaseRows())
        {
            if (!visibleRowSet.Contains(row))
            {
                row.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            row.Arrange(new Rect(0, y, finalSize.Width, row.AllocatedGroupHeight));
            y += row.AllocatedGroupHeight;

            if (row != visibleRows[^1] && dividerByTarget.TryGetValue(row, out var divider))
            {
                divider.Arrange(new Rect(0, y, finalSize.Width, BaseRowDividerHeight));
                y += BaseRowDividerHeight;
            }
        }

        foreach (var divider in Children.OfType<TelemetryBaseRowDivider>())
        {
            if (divider.TargetRow is null || !visibleRowSet.Contains(divider.TargetRow) || divider.TargetRow == visibleRows.LastOrDefault())
            {
                divider.Arrange(new Rect(0, 0, 0, 0));
            }
        }

        return finalSize;
    }

    private IReadOnlyList<TelemetryPlotRow> GetBaseRows()
        => Children.OfType<TelemetryPlotRow>().ToArray();

    private IReadOnlyList<TelemetryPlotRow> GetVisibleBaseRows()
        => GetBaseRows().Where(row => row.ReservesLayout).ToArray();

    private static double GetDividerTotalHeight(int visibleRowCount)
        => Math.Max(0, visibleRowCount - 1) * BaseRowDividerHeight;
}
