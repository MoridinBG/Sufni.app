using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.Presentation;

using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.Extensibility.Views;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Infrastructure;
namespace Sufni.App.Shared.Views.Controls;

public static class SignalRowExtensionHost
{
    public static readonly AttachedProperty<RecordedSessionExtensionSlots?> ExtensionSlotsProperty =
        AvaloniaProperty.RegisterAttached<SignalRow, RecordedSessionExtensionSlots?>(
            "ExtensionSlots",
            typeof(SignalRowExtensionHost));

    private static readonly AttachedProperty<RowState?> StateProperty =
        AvaloniaProperty.RegisterAttached<SignalRow, RowState?>(
            "State",
            typeof(SignalRowExtensionHost));

    private static readonly AttachedProperty<bool> IsHostedSignalRowProperty =
        AvaloniaProperty.RegisterAttached<SignalRow, bool>(
            "IsHostedSignalRow",
            typeof(SignalRowExtensionHost));

    static SignalRowExtensionHost()
    {
        ExtensionSlotsProperty.Changed.AddClassHandler<SignalRow>(OnExtensionSlotsChanged);
    }

    public static void SetExtensionSlots(SignalRow row, RecordedSessionExtensionSlots? value) =>
        row.SetValue(ExtensionSlotsProperty, value);

    public static RecordedSessionExtensionSlots? GetExtensionSlots(SignalRow row) =>
        row.GetValue(ExtensionSlotsProperty);

    internal static bool GetIsHostedSignalRow(SignalRow row) =>
        row.GetValue(IsHostedSignalRowProperty);

    private static void SetIsHostedSignalRow(SignalRow row, bool value) =>
        row.SetValue(IsHostedSignalRowProperty, value);

    private static void OnExtensionSlotsChanged(SignalRow row, AvaloniaPropertyChangedEventArgs args)
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

    private static void DisposeState(SignalRow row)
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
        private readonly SignalRow row;
        private readonly Dictionary<string, SignalRow> hostedRows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IRecordedSessionHostedSignalRowContributionViewModel> hostedRowViewModels = new(StringComparer.Ordinal);
        private RecordedSessionExtensionSlots? slots;
        private IEnumerable<SignalRowAction>? baseHeaderActions;
        private bool isApplyingHeaderActions;

        public RowState(SignalRow row)
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

            slots.SignalPlotContextMenuActions.CollectionChanged += OnPlotExtensionsChanged;
            slots.SignalRowHeaderActions.CollectionChanged += OnHeaderActionsChanged;
            slots.HostedSignalRows.CollectionChanged += OnHostedRowsChanged;
            slots.SignalTimeRangeOverlays.CollectionChanged += OnPlotExtensionsChanged;
        }

        private void UnsubscribeFromSlots()
        {
            if (slots is null)
            {
                return;
            }

            slots.SignalPlotContextMenuActions.CollectionChanged -= OnPlotExtensionsChanged;
            slots.SignalRowHeaderActions.CollectionChanged -= OnHeaderActionsChanged;
            slots.HostedSignalRows.CollectionChanged -= OnHostedRowsChanged;
            slots.SignalTimeRangeOverlays.CollectionChanged -= OnPlotExtensionsChanged;
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
            if (args.Property == SignalRow.HeaderActionsProperty)
            {
                if (isApplyingHeaderActions)
                {
                    return;
                }

                baseHeaderActions = row.HeaderActions;
                RebuildHeaderActions();
                return;
            }

            if (args.Property == SignalRow.RowIdProperty)
            {
                Rebuild();
                return;
            }

            if (args.Property == SignalRow.PlotContentProperty)
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
            SignalRowAction[] extensionActions = rowTargetStableKey is null || slots is null
                ? []
                : slots.SignalRowHeaderActions
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

        private void SetHeaderActions(IEnumerable<SignalRowAction>? actions)
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

            var contributions = slots.HostedSignalRows
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
                RemoveHostedRowViewModel(staleKey);
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
                    hostedRow = CreateHostedRow(key, contribution);
                    SetExtensionSlots(hostedRow, slots);
                    hostedRows.Add(key, hostedRow);
                }
                else
                {
                    UpdateHostedRow(key, hostedRow, contribution);
                }

                row.ChildRows.Insert(GetHostedRowInsertionIndex(row.ChildRows), hostedRow);
            }
        }

        private static int GetHostedRowInsertionIndex(IReadOnlyList<SignalRow> rows)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                if (!GetIsHostedSignalRow(rows[index]))
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
            foreach (var key in hostedRowViewModels.Keys.ToArray())
            {
                RemoveHostedRowViewModel(key);
            }
        }

        private SignalRow CreateHostedRow(
            string key,
            RecordedSessionHostedSignalRowContribution contribution)
        {
            var hostedRow = new SignalRow();
            UpdateHostedRow(key, hostedRow, contribution);
            return hostedRow;
        }

        private void UpdateHostedRow(
            string key,
            SignalRow hostedRow,
            RecordedSessionHostedSignalRowContribution contribution)
        {
            hostedRow.RowId = contribution.RowTarget.StableKey;
            SetIsHostedSignalRow(hostedRow, true);
            hostedRow.Title = contribution.Title;
            hostedRow.TitleToolTip = contribution.TitleToolTip;
            hostedRow.PresentationState = contribution.PresentationState;
            hostedRow.PlotContent = CreateOrUpdatePlotContent(key, hostedRow, contribution.ViewModel);
            hostedRow.PlaceholderContent = new SurfacePlaceholderCard { Title = contribution.Title };
            hostedRow.IsExpanded = contribution.IsInitiallyExpanded;
        }

        private Control CreateOrUpdatePlotContent(
            string key,
            SignalRow hostedRow,
            IRecordedSessionHostedSignalRowContributionViewModel viewModel)
        {
            SetHostedRowViewModel(key, viewModel);

            if (viewModel is RecordedSessionSignalPlotViewModel data)
            {
                if (hostedRow.PlotContent is ExtensionSignalPlotView existingView)
                {
                    existingView.DataContext = data;
                    return existingView;
                }

                return new ExtensionSignalPlotView { DataContext = data };
            }

            return ExtensionViewModelLifetime.CreateControl(viewModel);
        }

        private void SetHostedRowViewModel(
            string key,
            IRecordedSessionHostedSignalRowContributionViewModel viewModel)
        {
            if (hostedRowViewModels.TryGetValue(key, out var existing) &&
                ReferenceEquals(existing, viewModel))
            {
                return;
            }

            RemoveHostedRowViewModel(key);
            hostedRowViewModels[key] = viewModel;
        }

        private void RemoveHostedRowViewModel(string key)
        {
            hostedRowViewModels.Remove(key);
        }

        private static string GetContributionKey(RecordedSessionHostedSignalRowContribution contribution)
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
            plotView.AdditionalContextMenuActions = slots.SignalPlotContextMenuActions
                .Where(contribution => hasBuiltInRow && contribution.TargetRow == builtInRow)
                .OrderBy(contribution => contribution.Order)
                .Select(contribution => contribution.Action)
                .ToArray();
            plotView.TimeRangeOverlays = slots.SignalTimeRangeOverlays
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

        private static string? GetRowTargetStableKey(string? rowId)
        {
            if (TryGetBuiltInRow(rowId, out var builtInRow))
            {
                return RecordedSessionSignalRowTarget.BuiltIn(builtInRow).StableKey;
            }

            return string.IsNullOrWhiteSpace(rowId) ? null : rowId;
        }

        private static bool TryGetBuiltInRow(
            string? rowId,
            out RecordedSessionBuiltInSignalRow builtInRow)
        {
            builtInRow = rowId switch
            {
                SignalRowIds.Travel => RecordedSessionBuiltInSignalRow.Travel,
                SignalRowIds.Velocity => RecordedSessionBuiltInSignalRow.Velocity,
                SignalRowIds.Imu => RecordedSessionBuiltInSignalRow.Imu,
                SignalRowIds.PitchRoll => RecordedSessionBuiltInSignalRow.PitchRoll,
                SignalRowIds.Speed => RecordedSessionBuiltInSignalRow.Speed,
                SignalRowIds.Elevation => RecordedSessionBuiltInSignalRow.Elevation,
                _ => default,
            };
            return rowId is
                SignalRowIds.Travel or
                SignalRowIds.Velocity or
                SignalRowIds.Imu or
                SignalRowIds.PitchRoll or
                SignalRowIds.Speed or
                SignalRowIds.Elevation;
        }
    }
}
