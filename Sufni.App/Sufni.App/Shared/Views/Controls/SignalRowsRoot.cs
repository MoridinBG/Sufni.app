using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Theming;
using Sufni.App.Infrastructure.Theming;
namespace Sufni.App.Shared.Views.Controls;

public sealed class SignalRowsRoot : UserControl
{
    public static readonly StyledProperty<SignalLayoutPreferences> SignalLayoutPreferencesProperty =
        AvaloniaProperty.Register<SignalRowsRoot, SignalLayoutPreferences>(
            nameof(SignalLayoutPreferences),
            defaultValue: SignalLayoutPreferences.Default);

    // RootRowDropZoneHeight is derived from a spacing constant, which is theme-invariant.
    private static readonly double RootRowDropZoneHeight = SufniThemes.Fallback.Spacing.RootDropZoneHeight;
    private readonly ScrollViewer scrollViewer;
    private readonly SignalRowsPanel rowsPanel;
    private readonly Border rootDropIndicator;
    private readonly HashSet<SignalRow> watchedRows = [];
    private SignalRow? activeDraggedRow;
    private SignalRow? activeDropTargetRow;
    private bool applyingSignalLayoutPreferences;
    private bool publishingSignalLayoutPreferences;
    private IDisposable? themeVariantSubscription;

    public AvaloniaList<SignalRow> Rows { get; } = [];
    public SignalLayoutPreferences SignalLayoutPreferences
    {
        get => GetValue(SignalLayoutPreferencesProperty);
        set => SetValue(SignalLayoutPreferencesProperty, value);
    }

    internal bool IsRootDropIndicatorVisible => rootDropIndicator.IsVisible;
    internal double RootDropIndicatorY => rootDropIndicator.Margin.Top;

    static SignalRowsRoot()
    {
        SignalLayoutPreferencesProperty.Changed.AddClassHandler<SignalRowsRoot>((root, args) =>
        {
            if (!root.publishingSignalLayoutPreferences && args.NewValue is SignalLayoutPreferences preferences)
            {
                root.ApplySignalLayoutPreferences(preferences);
            }
        });
    }

    public SignalRowsRoot()
    {
        rowsPanel = new SignalRowsPanel
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
            Background = SufniThemes.Fallback.DragDrop.DropPositionIndicator.ToBrush(),
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplySignalLayoutPreferences(SignalLayoutPreferences);

        themeVariantSubscription = this.GetObservable(ThemeVariantScope.ActualThemeVariantProperty)
            .Subscribe(_ =>
                rootDropIndicator.Background = SufniThemes.FromVariant(ActualThemeVariant).DragDrop.DropPositionIndicator.ToBrush());
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        themeVariantSubscription?.Dispose();
        themeVariantSubscription = null;

        base.OnDetachedFromVisualTree(e);
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildRows();
        AttachRowListeners();
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
                rowsPanel.Children.Add(new SignalBaseRowDivider
                {
                    TargetRow = row,
                });
            }
        }

        rowsPanel.InvalidateMeasure();
    }

    private void AttachRowListeners()
    {
        foreach (var row in watchedRows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
            row.ChildRows.CollectionChanged -= OnRowChildrenChanged;
        }

        watchedRows.Clear();
        foreach (var row in Rows)
        {
            AttachRowListeners(row);
        }
    }

    private void AttachRowListeners(SignalRow row)
    {
        if (!watchedRows.Add(row))
        {
            return;
        }

        row.PropertyChanged += OnRowPropertyChanged;
        row.ChildRows.CollectionChanged += OnRowChildrenChanged;
        foreach (var childRow in row.ChildRows)
        {
            AttachRowListeners(childRow);
        }
    }

    private void OnRowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SignalRow.IsExpandedProperty)
        {
            PublishSignalLayoutPreferences();
        }
    }

    private void OnRowChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        AttachRowListeners();
        PublishSignalLayoutPreferences();
    }

    internal bool TryDropDraggedRow(SignalRow draggedRow, PointerReleasedEventArgs args)
    {
        return TryDropDraggedRowAtPoint(draggedRow, args.GetPosition(rowsPanel));
    }

    internal void BeginRowDragFeedback(SignalRow draggedRow)
    {
        if (ReferenceEquals(activeDraggedRow, draggedRow))
        {
            return;
        }

        ClearDragFeedback();
        activeDraggedRow = draggedRow;
        draggedRow.SetDragFeedback(true);
    }

    internal void UpdateRowDragFeedback(SignalRow draggedRow, PointerEventArgs args)
    {
        UpdateRowDragFeedbackAtPoint(draggedRow, args.GetPosition(rowsPanel));
    }

    internal void UpdateRowDragFeedbackAtPoint(SignalRow draggedRow, Point rowsPanelPoint)
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

    internal void EndRowDragFeedback(SignalRow draggedRow)
    {
        if (ReferenceEquals(activeDraggedRow, draggedRow))
        {
            draggedRow.SetDragFeedback(false);
            activeDraggedRow = null;
        }

        ClearDropTargetFeedback();
    }

    internal bool TryDropDraggedRowAtPoint(SignalRow draggedRow, Point rowsPanelPoint)
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

    internal bool MoveRowToRoot(SignalRow row, int targetIndex)
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

        if (!CanMovePreferenceToRoot(row, targetIndex))
        {
            return false;
        }

        source.RemoveAt(sourceIndex);
        row.ManualGroupHeight = null;
        row.ManualGroupHeightRatio = null;
        row.ApplyRootRowDefaults();
        Rows.Insert(Math.Clamp(targetIndex, 0, Rows.Count), row);
        InvalidateMeasure();
        PublishSignalLayoutPreferences();
        return true;
    }

    internal bool MoveRowInto(SignalRow row, SignalRow targetParent)
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

        if (!CanMovePreferenceInto(row, targetParent, targetParent.ChildRows.Count))
        {
            return false;
        }

        source.RemoveAt(sourceIndex);
        row.ManualGroupHeight = null;
        row.ManualGroupHeightRatio = null;
        row.ApplyHostedRowDefaults();
        targetParent.ChildRows.Add(row);
        targetParent.IsExpanded = true;
        InvalidateMeasure();
        PublishSignalLayoutPreferences();
        return true;
    }

    private bool CanMovePreferenceToRoot(SignalRow row, int targetIndex)
    {
        if (string.IsNullOrWhiteSpace(row.RowId))
        {
            return true;
        }

        var current = CaptureSignalLayoutPreferences();
        var moved = SignalLayoutPreferenceTree.MoveToRoot(current, row.RowId, targetIndex);
        return !moved.Equals(current);
    }

    private bool CanMovePreferenceInto(SignalRow row, SignalRow targetParent, int targetIndex)
    {
        if (string.IsNullOrWhiteSpace(row.RowId) ||
            string.IsNullOrWhiteSpace(targetParent.RowId))
        {
            return true;
        }

        var current = CaptureSignalLayoutPreferences();
        var moved = SignalLayoutPreferenceTree.MoveInto(current, row.RowId, targetParent.RowId, targetIndex);
        return !moved.Equals(current);
    }

    internal SignalLayoutPreferences CaptureSignalLayoutPreferences()
    {
        return SignalLayoutPreferenceTree.Capture(Rows
            .Where(row => !SignalRowExtensionHost.GetIsHostedSignalRow(row))
            .Select(CreateRowState));
    }

    private static SignalLayoutPreferenceRowState CreateRowState(SignalRow row)
    {
        return new SignalLayoutPreferenceRowState(
            row.RowId ?? "",
            row.IsExpanded,
            row.IsExpanded ? row.ManualGroupHeightRatio : null,
            row.ChildRows
                .Where(childRow => !SignalRowExtensionHost.GetIsHostedSignalRow(childRow))
                .Select(CreateRowState)
                .ToArray());
    }

    private void ApplySignalLayoutPreferences(SignalLayoutPreferences? preferences)
    {
        if (Rows.Count == 0)
        {
            return;
        }

        var allRows = GetAllRows().ToArray();
        if (!allRows.Any(row => !string.IsNullOrWhiteSpace(row.RowId)))
        {
            return;
        }

        var preferenceManagedRows = allRows
            .Where(row =>
                !SignalRowExtensionHost.GetIsHostedSignalRow(row) &&
                !string.IsNullOrWhiteSpace(row.RowId))
            .ToArray();
        var normalized = SignalLayoutPreferenceTree.Normalize(
            preferences,
            preferenceManagedRows.Select(row => row.RowId!));
        var rowsById = preferenceManagedRows
            .GroupBy(row => row.RowId!)
            .ToDictionary(group => group.Key, group => group.First());
        var usedIds = new HashSet<string>();
        var rootRows = new List<SignalRow>();

        applyingSignalLayoutPreferences = true;
        try
        {
            foreach (var row in allRows)
            {
                foreach (var childRow in row.ChildRows
                             .Where(childRow => !SignalRowExtensionHost.GetIsHostedSignalRow(childRow))
                             .ToArray())
                {
                    row.ChildRows.Remove(childRow);
                }
            }

            Rows.Clear();
            rootRows.AddRange(MaterializeRows(normalized.Rows, rowsById, usedIds));
            rootRows.AddRange(allRows.Where(row =>
                !SignalRowExtensionHost.GetIsHostedSignalRow(row) &&
                (string.IsNullOrWhiteSpace(row.RowId) || !usedIds.Contains(row.RowId))));

            foreach (var row in rootRows)
            {
                Rows.Add(row);
            }

            ResetIncompleteRowHeightPreferences();
        }
        finally
        {
            applyingSignalLayoutPreferences = false;
            RebuildRows();
            AttachRowListeners();
        }
    }

    private static IEnumerable<SignalRow> MaterializeRows(
        IEnumerable<SignalLayoutRowPreferences> preferences,
        IReadOnlyDictionary<string, SignalRow> rowsById,
        ISet<string> usedIds)
    {
        foreach (var preference in preferences)
        {
            if (string.IsNullOrWhiteSpace(preference.RowId) ||
                !rowsById.TryGetValue(preference.RowId, out var row) ||
                !usedIds.Add(preference.RowId))
            {
                continue;
            }

            row.IsExpanded = preference.IsExpanded;
            row.ManualGroupHeight = null;
            row.ManualGroupHeightRatio = preference.HeightRatio;
            foreach (var childRow in MaterializeRows(preference.Children, rowsById, usedIds))
            {
                row.ChildRows.Add(childRow);
            }

            yield return row;
        }
    }

    internal void CommitManualRowSizePreferences()
    {
        rowsPanel.CommitVisibleRowHeightRatios();
        PublishSignalLayoutPreferences();
    }

    private void ResetIncompleteRowHeightPreferences()
    {
        var resizableRootRows = Rows
            .Where(row => row.ReservesLayout && row.IsExpanded && row.GetVisiblePlotSlotCount() > 0)
            .ToArray();
        if (resizableRootRows.Length == 0)
        {
            return;
        }

        if (resizableRootRows.Any(row =>
                row.ManualGroupHeightRatio is not { } ratio ||
                !double.IsFinite(ratio) ||
                ratio <= 0))
        {
            rowsPanel.ClearManualRowSizing();
        }
    }

    private void PublishSignalLayoutPreferences()
    {
        if (applyingSignalLayoutPreferences)
        {
            return;
        }

        publishingSignalLayoutPreferences = true;
        try
        {
            SignalLayoutPreferences = CaptureSignalLayoutPreferences();
        }
        finally
        {
            publishingSignalLayoutPreferences = false;
        }
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

    private bool TryGetHeaderDropTarget(SignalRow draggedRow, Point rowsPanelPoint, out SignalRow targetRow)
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

    private AvaloniaList<SignalRow>? FindOwningCollection(SignalRow row)
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

    private static AvaloniaList<SignalRow>? FindOwningChildCollection(
        SignalRow parent,
        SignalRow row)
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

    private IEnumerable<SignalRow> GetAllRows()
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

    private static IEnumerable<SignalRow> GetDescendantRows(SignalRow row)
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
