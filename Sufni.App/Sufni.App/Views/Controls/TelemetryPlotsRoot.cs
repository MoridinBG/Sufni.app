using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Sufni.App.Views.Controls;

public sealed class TelemetryPlotsRoot : UserControl
{
    private const double RootRowDropZoneHeight = 12;
    private readonly ScrollViewer scrollViewer;
    private readonly TelemetryPlotRowsPanel rowsPanel;
    private readonly Border rootDropIndicator;
    private TelemetryPlotRow? activeDraggedRow;
    private TelemetryPlotRow? activeDropTargetRow;

    public AvaloniaList<TelemetryPlotRow> Rows { get; } = [];
    internal bool IsRootDropIndicatorVisible => rootDropIndicator.IsVisible;
    internal double RootDropIndicatorY => rootDropIndicator.Margin.Top;

    public TelemetryPlotsRoot()
    {
        rowsPanel = new TelemetryPlotRowsPanel
        {
            Name = "RowsPanel",
        };
        scrollViewer = new ScrollViewer
        {
            Name = "RowsScrollViewer",
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            IsScrollChainingEnabled = false,
            Content = rowsPanel,
        };
        rootDropIndicator = new Border
        {
            Name = "RootDropIndicator",
            Height = 3,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.Parse("#2A95D8")),
            IsHitTestVisible = false,
            IsVisible = false,
        };
        Content = new Grid
        {
            ClipToBounds = true,
            Children =
            {
                scrollViewer,
                rootDropIndicator,
            },
        };

        Rows.CollectionChanged += OnRowsChanged;
        RebuildRows();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        rowsPanel.ViewportHeightOverride = double.IsInfinity(availableSize.Height)
            ? null
            : availableSize.Height;
        return base.MeasureOverride(availableSize);
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildRows();
    }

    private void RebuildRows()
    {
        rowsPanel.Children.Clear();

        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            row.ApplyRootRowDefaults();
            rowsPanel.Children.Add(row);

            if (i < Rows.Count - 1)
            {
                rowsPanel.Children.Add(new TelemetryBaseRowDivider
                {
                    TargetRow = row,
                });
            }
        }

        rowsPanel.InvalidateMeasure();
    }

    internal bool TryDropDraggedRow(TelemetryPlotRow draggedRow, PointerReleasedEventArgs args)
    {
        return TryDropDraggedRowAtPoint(draggedRow, args.GetPosition(rowsPanel));
    }

    internal void BeginRowDragFeedback(TelemetryPlotRow draggedRow)
    {
        if (ReferenceEquals(activeDraggedRow, draggedRow))
        {
            return;
        }

        ClearDragFeedback();
        activeDraggedRow = draggedRow;
        draggedRow.SetDragFeedback(true);
    }

    internal void UpdateRowDragFeedback(TelemetryPlotRow draggedRow, PointerEventArgs args)
    {
        UpdateRowDragFeedbackAtPoint(draggedRow, args.GetPosition(rowsPanel));
    }

    internal void UpdateRowDragFeedbackAtPoint(TelemetryPlotRow draggedRow, Point rowsPanelPoint)
    {
        if (!ReferenceEquals(activeDraggedRow, draggedRow))
        {
            BeginRowDragFeedback(draggedRow);
        }

        ClearDropTargetFeedback();

        if (TryGetHeaderDropTarget(draggedRow, rowsPanelPoint, out var targetRow))
        {
            activeDropTargetRow = targetRow;
            activeDropTargetRow.SetDropTargetFeedback(true);
            return;
        }

        if (TryGetRootInsertPosition(rowsPanelPoint, out _, out var previewY))
        {
            ShowRootDropIndicator(previewY);
        }
    }

    internal void EndRowDragFeedback(TelemetryPlotRow draggedRow)
    {
        if (ReferenceEquals(activeDraggedRow, draggedRow))
        {
            draggedRow.SetDragFeedback(false);
            activeDraggedRow = null;
        }

        ClearDropTargetFeedback();
    }

    internal bool TryDropDraggedRowAtPoint(TelemetryPlotRow draggedRow, Point rowsPanelPoint)
    {
        if (TryGetHeaderDropTarget(draggedRow, rowsPanelPoint, out var targetRow))
        {
            return MoveRowInto(draggedRow, targetRow);
        }

        if (TryGetRootInsertIndex(rowsPanelPoint, out var rootInsertIndex))
        {
            return MoveRowToRoot(draggedRow, rootInsertIndex);
        }

        return false;
    }

    private void ClearDragFeedback()
    {
        activeDraggedRow?.SetDragFeedback(false);
        activeDraggedRow = null;
        ClearDropTargetFeedback();
    }

    private void ClearDropTargetFeedback()
    {
        activeDropTargetRow?.SetDropTargetFeedback(false);
        activeDropTargetRow = null;
        rootDropIndicator.IsVisible = false;
    }

    private void ShowRootDropIndicator(double rowsPanelY)
    {
        var viewportY = rowsPanelY - scrollViewer.Offset.Y - rootDropIndicator.Height / 2;
        rootDropIndicator.Margin = new Thickness(0, Math.Max(0, viewportY), 0, 0);
        rootDropIndicator.IsVisible = true;
    }

    internal bool MoveRowToRoot(TelemetryPlotRow row, int targetIndex)
    {
        var source = FindOwningCollection(row);
        if (source is null)
        {
            return false;
        }

        var sourceIndex = source.IndexOf(row);
        if (sourceIndex < 0)
        {
            return false;
        }

        if (ReferenceEquals(source, Rows) && sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        targetIndex = Math.Clamp(targetIndex, 0, Rows.Count - (ReferenceEquals(source, Rows) ? 1 : 0));
        if (ReferenceEquals(source, Rows) && sourceIndex == targetIndex)
        {
            return false;
        }

        source.RemoveAt(sourceIndex);
        row.ManualGroupHeight = null;
        row.ApplyRootRowDefaults();
        Rows.Insert(Math.Clamp(targetIndex, 0, Rows.Count), row);
        InvalidateMeasure();
        return true;
    }

    internal bool MoveRowInto(TelemetryPlotRow row, TelemetryPlotRow targetParent)
    {
        if (ReferenceEquals(row, targetParent) || row.HasDescendant(targetParent))
        {
            return false;
        }

        var source = FindOwningCollection(row);
        if (source is null)
        {
            return false;
        }

        var sourceIndex = source.IndexOf(row);
        if (sourceIndex < 0)
        {
            return false;
        }

        if (ReferenceEquals(source, targetParent.ChildRows) && sourceIndex == targetParent.ChildRows.Count - 1)
        {
            return false;
        }

        source.RemoveAt(sourceIndex);
        row.ManualGroupHeight = null;
        row.ApplyHostedRowDefaults();
        targetParent.ChildRows.Add(row);
        targetParent.IsExpanded = true;
        InvalidateMeasure();
        return true;
    }

    private bool TryGetRootInsertIndex(Point rowsPanelPoint, out int targetIndex)
    {
        return TryGetRootInsertPosition(rowsPanelPoint, out targetIndex, out _);
    }

    private bool TryGetRootInsertPosition(Point rowsPanelPoint, out int targetIndex, out double previewY)
    {
        var visibleRows = Rows.Where(row => row.ReservesLayout).ToArray();
        if (visibleRows.Length == 0)
        {
            targetIndex = Rows.Count;
            previewY = 0;
            return true;
        }

        foreach (var row in visibleRows)
        {
            var rowIndex = Rows.IndexOf(row);
            if (rowsPanelPoint.Y <= row.Bounds.Y + RootRowDropZoneHeight)
            {
                targetIndex = rowIndex;
                previewY = row.Bounds.Y;
                return true;
            }

            if (Math.Abs(rowsPanelPoint.Y - row.Bounds.Bottom) <= RootRowDropZoneHeight)
            {
                targetIndex = rowIndex + 1;
                previewY = row.Bounds.Bottom;
                return true;
            }
        }

        var lastRow = visibleRows[^1];
        if (rowsPanelPoint.Y > lastRow.Bounds.Bottom)
        {
            targetIndex = Rows.IndexOf(lastRow) + 1;
            previewY = lastRow.Bounds.Bottom;
            return true;
        }

        targetIndex = -1;
        previewY = 0;
        return false;
    }

    private bool TryGetHeaderDropTarget(TelemetryPlotRow draggedRow, Point rowsPanelPoint, out TelemetryPlotRow targetRow)
    {
        foreach (var row in GetAllRows())
        {
            if (ReferenceEquals(row, draggedRow) ||
                draggedRow.HasDescendant(row) ||
                !row.HeaderContainsPoint(rowsPanelPoint, rowsPanel))
            {
                continue;
            }

            targetRow = row;
            return true;
        }

        targetRow = null!;
        return false;
    }

    private AvaloniaList<TelemetryPlotRow>? FindOwningCollection(TelemetryPlotRow row)
    {
        if (Rows.Contains(row))
        {
            return Rows;
        }

        foreach (var rootRow in Rows)
        {
            var collection = FindOwningChildCollection(rootRow, row);
            if (collection is not null)
            {
                return collection;
            }
        }

        return null;
    }

    private static AvaloniaList<TelemetryPlotRow>? FindOwningChildCollection(
        TelemetryPlotRow parent,
        TelemetryPlotRow row)
    {
        if (parent.ChildRows.Contains(row))
        {
            return parent.ChildRows;
        }

        foreach (var childRow in parent.ChildRows)
        {
            var collection = FindOwningChildCollection(childRow, row);
            if (collection is not null)
            {
                return collection;
            }
        }

        return null;
    }

    private IEnumerable<TelemetryPlotRow> GetAllRows()
    {
        foreach (var row in Rows)
        {
            yield return row;
            foreach (var childRow in GetDescendantRows(row))
            {
                yield return childRow;
            }
        }
    }

    private static IEnumerable<TelemetryPlotRow> GetDescendantRows(TelemetryPlotRow row)
    {
        foreach (var childRow in row.ChildRows)
        {
            yield return childRow;
            foreach (var descendantRow in GetDescendantRows(childRow))
            {
                yield return descendantRow;
            }
        }
    }
}
