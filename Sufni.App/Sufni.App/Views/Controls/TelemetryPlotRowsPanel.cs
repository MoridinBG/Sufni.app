using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Sufni.App.Theming;

namespace Sufni.App.Views.Controls;

internal sealed class TelemetryPlotRowsPanel : Panel
{
    internal static readonly double BaseRowDividerHeight = SufniThemes.Fallback.Spacing.BaseRowDividerHeight;
    private static readonly TimeSpan RowHeightAnimationDuration = TimeSpan.FromMilliseconds(160);

    private readonly Dictionary<TelemetryPlotRow, AnimatedRowHeight> rowHeights = [];
    private readonly DispatcherTimer animationTimer;
    private double? viewportHeightOverride;

    public TelemetryPlotRowsPanel()
    {
        animationTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        animationTimer.Tick += (_, _) => AdvanceAnimations();
    }

    internal double? ViewportHeightOverride
    {
        get => viewportHeightOverride;
        set
        {
            if (viewportHeightOverride == value)
            {
                return;
            }

            viewportHeightOverride = value;
            InvalidateMeasure();
        }
    }

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

        var preferredTotal = preferredRowsHeight;
        var viewportHeight = ViewportHeightOverride;
        var extra = viewportHeight is { } finiteHeight
            ? Math.Max(0, finiteHeight - preferredTotal)
            : 0;
        var extraPerAutoRow = autoGrowRows.Count > 0 ? extra / autoGrowRows.Count : 0;
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var baseRows = GetBaseRows();

        RemoveStaleAnimations(baseRows);
        foreach (var row in baseRows)
        {
            if (!row.ReservesLayout)
            {
                row.ApplyAllocatedGroupHeight(0);
                SetDisplayedHeightTarget(row, 0);
                row.Measure(new Size(width, 0));
                continue;
            }

            var targetHeight = row.ManualGroupHeight ?? row.GetPreferredGroupHeight();
            if (autoGrowRows.Contains(row))
            {
                targetHeight += extraPerAutoRow;
            }

            var actualHeight = row.ApplyAllocatedGroupHeight(targetHeight);
            SetDisplayedHeightTarget(row, actualHeight);
            row.Measure(new Size(width, actualHeight));
        }

        foreach (var divider in Children.OfType<TelemetryBaseRowDivider>())
        {
            var isVisibleDivider = IsBoundaryDivider(divider, visibleRows);
            divider.CanResizeTargetRow = isVisibleDivider && IsResizableBoundaryTarget(divider.TargetRow);
            divider.IsVisible = isVisibleDivider;
            divider.Measure(new Size(width, isVisibleDivider ? BaseRowDividerHeight : 0));
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

            var displayedHeight = GetDisplayedHeight(row);
            row.Arrange(new Rect(0, y, finalSize.Width, displayedHeight));
            y += displayedHeight;

            if (dividerByTarget.TryGetValue(row, out var divider) && IsBoundaryDivider(divider, visibleRows))
            {
                divider.Arrange(new Rect(0, y - BaseRowDividerHeight, finalSize.Width, BaseRowDividerHeight));
            }
        }

        foreach (var divider in Children.OfType<TelemetryBaseRowDivider>())
        {
            if (!IsBoundaryDivider(divider, visibleRows))
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

    private static bool IsBoundaryDivider(TelemetryBaseRowDivider divider, IReadOnlyList<TelemetryPlotRow> visibleRows)
    {
        var targetRow = divider.TargetRow;
        return targetRow is not null &&
               IndexOf(visibleRows, targetRow) is var index &&
               index >= 0 &&
               index < visibleRows.Count - 1;
    }

    private static bool IsResizableBoundaryTarget(TelemetryPlotRow? row)
        => row is { IsExpanded: true } && row.GetVisiblePlotSlotCount() > 0;

    private double GetDisplayedHeight(TelemetryPlotRow row)
        => rowHeights.TryGetValue(row, out var height)
            ? height.Current
            : row.AllocatedGroupHeight;

    private void SetDisplayedHeightTarget(TelemetryPlotRow row, double target)
    {
        if (!rowHeights.TryGetValue(row, out var height))
        {
            rowHeights[row] = new AnimatedRowHeight(target);
            return;
        }

        if (Math.Abs(height.Target - target) < 0.5)
        {
            return;
        }

        height.Start = height.Current;
        height.Target = target;
        height.StartTimestamp = Stopwatch.GetTimestamp();
        rowHeights[row] = height;
        animationTimer.Start();
    }

    private void AdvanceAnimations()
    {
        var hasActiveAnimations = false;

        foreach (var entry in rowHeights.ToArray())
        {
            var height = entry.Value;
            var elapsed = Stopwatch.GetElapsedTime(height.StartTimestamp);
            var progress = Math.Clamp(elapsed.TotalMilliseconds / RowHeightAnimationDuration.TotalMilliseconds, 0, 1);
            var easedProgress = 1 - Math.Pow(1 - progress, 3);
            height.Current = height.Start + (height.Target - height.Start) * easedProgress;

            if (progress >= 1)
            {
                height.Current = height.Target;
            }
            else
            {
                hasActiveAnimations = true;
            }

            rowHeights[entry.Key] = height;
        }

        InvalidateArrange();
        if (!hasActiveAnimations)
        {
            animationTimer.Stop();
        }
    }

    private void RemoveStaleAnimations(IReadOnlyList<TelemetryPlotRow> baseRows)
    {
        foreach (var row in rowHeights.Keys.ToArray())
        {
            if (!baseRows.Contains(row))
            {
                rowHeights.Remove(row);
            }
        }
    }

    private sealed class AnimatedRowHeight(double height)
    {
        public double Start { get; set; } = height;
        public double Target { get; set; } = height;
        public double Current { get; set; } = height;
        public long StartTimestamp { get; set; } = Stopwatch.GetTimestamp();
    }

    private static int IndexOf(IReadOnlyList<TelemetryPlotRow> rows, TelemetryPlotRow targetRow)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (ReferenceEquals(rows[i], targetRow))
            {
                return i;
            }
        }

        return -1;
    }
}
