using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.ExtensionHost;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.Models;
using Sufni.App.ExtensionHost.Views.Controls;

namespace Sufni.App.Views.Controls;

public static class TelemetryPlotRowExtensionHost
{
    public static readonly AttachedProperty<RecordedSessionExtensionSlots?> ExtensionSlotsProperty =
        AvaloniaProperty.RegisterAttached<TelemetryPlotRow, RecordedSessionExtensionSlots?>(
            "ExtensionSlots",
            typeof(TelemetryPlotRowExtensionHost));

    private static readonly AttachedProperty<RowState?> StateProperty =
        AvaloniaProperty.RegisterAttached<TelemetryPlotRow, RowState?>(
            "State",
            typeof(TelemetryPlotRowExtensionHost));

    private static readonly AttachedProperty<bool> IsHostedGraphRowProperty =
        AvaloniaProperty.RegisterAttached<TelemetryPlotRow, bool>(
            "IsHostedGraphRow",
            typeof(TelemetryPlotRowExtensionHost));

    static TelemetryPlotRowExtensionHost()
    {
        ExtensionSlotsProperty.Changed.AddClassHandler<TelemetryPlotRow>(OnExtensionSlotsChanged);
    }

    public static void SetExtensionSlots(TelemetryPlotRow row, RecordedSessionExtensionSlots? value) =>
        row.SetValue(ExtensionSlotsProperty, value);

    public static RecordedSessionExtensionSlots? GetExtensionSlots(TelemetryPlotRow row) =>
        row.GetValue(ExtensionSlotsProperty);

    internal static bool GetIsHostedGraphRow(TelemetryPlotRow row) =>
        row.GetValue(IsHostedGraphRowProperty);

    private static void SetIsHostedGraphRow(TelemetryPlotRow row, bool value) =>
        row.SetValue(IsHostedGraphRowProperty, value);

    private static void OnExtensionSlotsChanged(TelemetryPlotRow row, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is not RecordedSessionExtensionSlots slots)
        {
            DisposeState(row);
            return;
        }

        var state = row.GetValue(StateProperty);
        if (state is null)
        {
            state = new RowState(row);
            row.SetValue(StateProperty, state);
        }

        state.SetSlots(slots);
    }

    private static void DisposeState(TelemetryPlotRow row)
    {
        var state = row.GetValue(StateProperty);
        if (state is null)
        {
            return;
        }

        state.Dispose();
        row.SetValue(StateProperty, null);
    }

    private sealed class RowState : IDisposable
    {
        private readonly TelemetryPlotRow row;
        private readonly Dictionary<string, TelemetryPlotRow> hostedRows = new(StringComparer.Ordinal);
        private RecordedSessionExtensionSlots? slots;
        private IEnumerable<TelemetryPlotRowAction>? baseHeaderActions;
        private bool isApplyingHeaderActions;

        public RowState(TelemetryPlotRow row)
        {
            this.row = row;
            row.PropertyChanged += OnRowPropertyChanged;
        }

        public void SetSlots(RecordedSessionExtensionSlots value)
        {
            if (ReferenceEquals(slots, value))
            {
                Rebuild();
                return;
            }

            UnsubscribeFromSlots();
            slots = value;
            baseHeaderActions = row.HeaderActions;
            SubscribeToSlots();
            Rebuild();
        }

        public void Dispose()
        {
            UnsubscribeFromSlots();
            row.PropertyChanged -= OnRowPropertyChanged;
            ClearHostedRows();
            ClearPlotExtensions();
            RestoreBaseHeaderActions();
            slots = null;
        }

        private void SubscribeToSlots()
        {
            if (slots is null)
            {
                return;
            }

            slots.PlotContextMenuActions.CollectionChanged += OnPlotExtensionsChanged;
            slots.PlotRowHeaderActions.CollectionChanged += OnHeaderActionsChanged;
            slots.HostedGraphRows.CollectionChanged += OnHostedRowsChanged;
            slots.TimeRangeOverlays.CollectionChanged += OnPlotExtensionsChanged;
        }

        private void UnsubscribeFromSlots()
        {
            if (slots is null)
            {
                return;
            }

            slots.PlotContextMenuActions.CollectionChanged -= OnPlotExtensionsChanged;
            slots.PlotRowHeaderActions.CollectionChanged -= OnHeaderActionsChanged;
            slots.HostedGraphRows.CollectionChanged -= OnHostedRowsChanged;
            slots.TimeRangeOverlays.CollectionChanged -= OnPlotExtensionsChanged;
        }

        private void OnHeaderActionsChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            RebuildHeaderActions();
        }

        private void OnHostedRowsChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            RebuildHostedRows();
        }

        private void OnPlotExtensionsChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            RebuildPlotExtensions();
        }

        private void OnRowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == TelemetryPlotRow.HeaderActionsProperty)
            {
                if (isApplyingHeaderActions)
                {
                    return;
                }

                baseHeaderActions = row.HeaderActions;
                RebuildHeaderActions();
                return;
            }

            if (args.Property == TelemetryPlotRow.RowIdProperty)
            {
                Rebuild();
                return;
            }

            if (args.Property == TelemetryPlotRow.PlotContentProperty)
            {
                RebuildPlotExtensions();
            }
        }

        private void Rebuild()
        {
            RebuildHeaderActions();
            RebuildHostedRows();
            RebuildPlotExtensions();
        }

        private void RebuildHeaderActions()
        {
            var rowId = row.RowId;
            var rowTargetStableKey = GetRowTargetStableKey(rowId);
            TelemetryPlotRowAction[] extensionActions = rowTargetStableKey is null || slots is null
                ? []
                : slots.PlotRowHeaderActions
                    .Where(contribution => contribution.TargetRow.StableKey == rowTargetStableKey)
                    .OrderBy(contribution => contribution.Order)
                    .Select(contribution => contribution.Action)
                    .ToArray();

            if (extensionActions.Length == 0)
            {
                RestoreBaseHeaderActions();
                return;
            }

            var merged = (baseHeaderActions ?? []).Concat(extensionActions).ToArray();
            SetHeaderActions(merged);
        }

        private void RestoreBaseHeaderActions()
        {
            SetHeaderActions(baseHeaderActions);
        }

        private void SetHeaderActions(IEnumerable<TelemetryPlotRowAction>? actions)
        {
            isApplyingHeaderActions = true;
            try
            {
                row.HeaderActions = actions;
            }
            finally
            {
                isApplyingHeaderActions = false;
            }
        }

        private void RebuildHostedRows()
        {
            var rowId = row.RowId;
            if (slots is null || !TryGetBuiltInRow(rowId, out var parentRow))
            {
                ClearHostedRows();
                return;
            }

            var contributions = slots.HostedGraphRows
                .Where(contribution => contribution.ParentRow == parentRow)
                .OrderBy(contribution => contribution.Order)
                .ToArray();
            var contributionKeys = contributions
                .Select(GetContributionKey)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var staleKey in hostedRows.Keys.Where(key => !contributionKeys.Contains(key)).ToArray())
            {
                var staleRow = hostedRows[staleKey];
                row.ChildRows.Remove(staleRow);
                DisposeState(staleRow);
                hostedRows.Remove(staleKey);
            }

            foreach (var hostedRow in hostedRows.Values)
            {
                row.ChildRows.Remove(hostedRow);
            }

            foreach (var contribution in contributions)
            {
                var key = GetContributionKey(contribution);
                if (!hostedRows.TryGetValue(key, out var hostedRow))
                {
                    hostedRow = CreateHostedRow(contribution);
                    SetExtensionSlots(hostedRow, slots);
                    hostedRows.Add(key, hostedRow);
                }
                else
                {
                    UpdateHostedRow(hostedRow, contribution);
                }

                row.ChildRows.Insert(GetHostedRowInsertionIndex(row.ChildRows), hostedRow);
            }
        }

        private static int GetHostedRowInsertionIndex(IReadOnlyList<TelemetryPlotRow> rows)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                if (!GetIsHostedGraphRow(rows[index]))
                {
                    return index;
                }
            }

            return rows.Count;
        }

        private void ClearHostedRows()
        {
            foreach (var hostedRow in hostedRows.Values)
            {
                row.ChildRows.Remove(hostedRow);
                DisposeState(hostedRow);
            }

            hostedRows.Clear();
        }

        private static TelemetryPlotRow CreateHostedRow(RecordedSessionHostedGraphRowContribution contribution)
        {
            var hostedRow = new TelemetryPlotRow();
            UpdateHostedRow(hostedRow, contribution);
            return hostedRow;
        }

        private static void UpdateHostedRow(
            TelemetryPlotRow hostedRow,
            RecordedSessionHostedGraphRowContribution contribution)
        {
            hostedRow.RowId = contribution.RowTarget.StableKey;
            SetIsHostedGraphRow(hostedRow, true);
            hostedRow.Title = contribution.Title;
            hostedRow.TitleToolTip = contribution.TitleToolTip;
            hostedRow.PresentationState = contribution.PresentationState;
            hostedRow.PlotContent = CreateContributionControl(contribution.ViewModel);
            hostedRow.PlaceholderContent = new SurfacePlaceholderCard { Title = contribution.Title };
            hostedRow.IsExpanded = contribution.IsInitiallyExpanded;
        }

        private static string GetContributionKey(RecordedSessionHostedGraphRowContribution contribution)
        {
            return $"{contribution.ExtensionId}:{contribution.ContributionId}";
        }

        private void RebuildPlotExtensions()
        {
            var rowId = row.RowId;
            var rowTargetStableKey = GetRowTargetStableKey(rowId);
            if (row.PlotContent is not SufniTimeSeriesPlotView plotView || rowTargetStableKey is null || slots is null)
            {
                ClearPlotExtensions();
                return;
            }

            var hasBuiltInRow = TryGetBuiltInRow(rowId, out var builtInRow);
            plotView.AdditionalContextMenuActions = slots.PlotContextMenuActions
                .Where(contribution => hasBuiltInRow && contribution.TargetRow == builtInRow)
                .OrderBy(contribution => contribution.Order)
                .Select(contribution => contribution.Action)
                .ToArray();
            plotView.TimeRangeOverlays = slots.TimeRangeOverlays
                .Where(contribution => contribution.TargetRow.StableKey == rowTargetStableKey)
                .OrderBy(contribution => contribution.Order)
                .Select(contribution => contribution.Registration)
                .ToArray();
        }

        private void ClearPlotExtensions()
        {
            if (row.PlotContent is not SufniTimeSeriesPlotView plotView)
            {
                return;
            }

            plotView.AdditionalContextMenuActions = null;
            plotView.TimeRangeOverlays = null;
        }

        private static Control CreateContributionControl(IExtensionViewModel viewModel)
        {
            return viewModel as Control ?? new ContentControl { Content = viewModel };
        }

        private static string? GetRowTargetStableKey(string? rowId)
        {
            if (TryGetBuiltInRow(rowId, out var builtInRow))
            {
                return RecordedSessionGraphRowTarget.BuiltIn(builtInRow).StableKey;
            }

            return string.IsNullOrWhiteSpace(rowId) ? null : rowId;
        }

        private static bool TryGetBuiltInRow(
            string? rowId,
            out RecordedSessionBuiltInGraphRow builtInRow)
        {
            builtInRow = rowId switch
            {
                TelemetryGraphRowIds.Travel => RecordedSessionBuiltInGraphRow.Travel,
                TelemetryGraphRowIds.Velocity => RecordedSessionBuiltInGraphRow.Velocity,
                TelemetryGraphRowIds.Imu => RecordedSessionBuiltInGraphRow.Imu,
                TelemetryGraphRowIds.PitchRoll => RecordedSessionBuiltInGraphRow.PitchRoll,
                TelemetryGraphRowIds.Speed => RecordedSessionBuiltInGraphRow.Speed,
                TelemetryGraphRowIds.Elevation => RecordedSessionBuiltInGraphRow.Elevation,
                _ => default,
            };
            return rowId is
                TelemetryGraphRowIds.Travel or
                TelemetryGraphRowIds.Velocity or
                TelemetryGraphRowIds.Imu or
                TelemetryGraphRowIds.PitchRoll or
                TelemetryGraphRowIds.Speed or
                TelemetryGraphRowIds.Elevation;
        }
    }
}
