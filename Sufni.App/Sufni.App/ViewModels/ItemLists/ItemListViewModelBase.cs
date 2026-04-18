using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sufni.App.ViewModels.ItemLists;

/// <summary>
/// Shared search-bar / date-filter / menu-item state for the entity
/// list view models. After Slice 3 every concrete list owns its own
/// store-backed projection and exposes it via a `new` shadow on
/// <c>Items</c>; the base contributes only the cross-cutting filter
/// state and the menu-item collection.
///
/// The base also owns the pending-delete UX (3-second undo window).
/// Subclasses install their own pending state, call
/// <see cref="FlushPendingDeleteAsync"/> to commit any in-flight
/// delete first, then call <see cref="StartUndoWindow"/> with a
/// finalize callback that persists the delete through the appropriate
/// coordinator and surfaces error messages. The base owns
/// <see cref="PendingName"/>, <see cref="IsUndoVisible"/> and the
/// undo / finalize commands that the shared <c>UndoDeleteButton</c>
/// control binds against.
/// </summary>
public partial class ItemListViewModelBase : ViewModelBase
{
    #region Constants

    private const int PendingDeleteWindowMs = 3000;

    #endregion Constants

    #region Private fields

    private CancellationTokenSource? pendingDeleteCts;
    private Func<Task>? pendingFinalize;

    #endregion Private fields

    #region Observable properties

    [ObservableProperty] private string? searchText;
    [ObservableProperty] private bool searchBoxIsFocused;
    [ObservableProperty] private DateTime? dateFilterFrom;
    [ObservableProperty] private DateTime? dateFilterTo;
    [ObservableProperty] private bool dateFilterVisible;
    [ObservableProperty] private string? pendingName;
    [ObservableProperty] private bool isUndoVisible;

    public ObservableCollection<PullMenuItemViewModel> MenuItems { get; set; } = [];

    #endregion Observable properties

    #region Property change handlers

    partial void OnSearchBoxIsFocusedChanged(bool value)
    {
        if (value)
        {
            DateFilterVisible = true;
        }
    }

    #endregion Property change handlers

    #region Virtual methods

    protected virtual void AddImplementation() { }

    /// <summary>
    /// Republish the filter predicate so the row hide/reveal triggered
    /// by a pending delete or undo is observed by the DynamicData chain.
    /// Subclasses must override.
    /// </summary>
    protected virtual void RebuildFilter() { }

    protected async Task RunPendingDeleteInteractionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ErrorMessages.Add(exception.Message);
        }
    }

    #endregion Virtual methods

    #region Pending-delete helpers

    /// <summary>
    /// True if there is a pending delete in flight.
    /// </summary>
    protected bool HasPendingDelete => pendingDeleteCts is not null;

    /// <summary>
    /// Commit any in-flight delete immediately. Subclasses call this
    /// before installing a new pending entry, so the previous one is
    /// finalized first (matches the legacy behavior when a second
    /// delete arrives during the undo window).
    /// </summary>
    protected async Task FlushPendingDeleteAsync()
    {
        var cts = pendingDeleteCts;
        var finalize = pendingFinalize;
        if (cts is null) return;

        pendingDeleteCts = null;
        pendingFinalize = null;
        cts.Cancel();
        cts.Dispose();

        PendingName = null;
        IsUndoVisible = false;

        if (finalize is null)
        {
            return;
        }

        try
        {
            await finalize();
        }
        catch (Exception exception)
        {
            ErrorMessages.Add(exception.Message);
        }
    }

    /// <summary>
    /// Start a fresh 3-second undo window. The supplied
    /// <paramref name="finalize"/> callback runs on timer expiry (or
    /// when a later delete supersedes this one). Caller is responsible
    /// for installing the row's pending state and calling
    /// <see cref="RebuildFilter"/> before invoking this — the base does
    /// not touch concrete pending state, only the user-visible undo
    /// surface and the timer.
    /// </summary>
    protected void StartUndoWindow(string displayName, Func<Task> finalize)
    {
        PendingName = displayName;
        IsUndoVisible = true;
        pendingFinalize = finalize;

        var cts = new CancellationTokenSource();
        pendingDeleteCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(PendingDeleteWindowMs, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (!ReferenceEquals(pendingDeleteCts, cts)) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(FlushPendingDeleteAsync);
        });
    }

    /// <summary>
    /// Cancel an in-flight pending delete without invoking its
    /// finalize callback. Subclasses override to clear their own
    /// pending state and republish the filter.
    /// </summary>
    protected virtual void OnPendingDeleteUndone() { }

    #endregion Pending-delete helpers

    #region Commands

    [RelayCommand]
    private void Add()
    {
        AddImplementation();
    }

    [RelayCommand]
    private void ClearSearchText()
    {
        SearchText = null;
        DateFilterVisible = false;
    }

    [RelayCommand]
    private void ClearDateFilter(string which)
    {
        switch (which)
        {
            case "from":
                DateFilterFrom = null;
                break;
            case "to":
                DateFilterTo = null;
                break;
        }
    }

    [RelayCommand]
    private void ToggleDateFilter()
    {
        DateFilterVisible = !DateFilterVisible;
    }

    /// <summary>
    /// Cancel the in-flight delete and restore the row. Bound from the
    /// "undo" button in the shared <c>UndoDeleteButton</c> control.
    /// </summary>
    [RelayCommand]
    private void UndoDelete()
    {
        var cts = pendingDeleteCts;
        if (cts is null) return;

        pendingDeleteCts = null;
        pendingFinalize = null;
        cts.Cancel();
        cts.Dispose();

        PendingName = null;
        IsUndoVisible = false;

        OnPendingDeleteUndone();
    }

    /// <summary>
    /// Commit the in-flight delete immediately (e.g. dismiss button on
    /// the undo bar). Bound from the dismiss button in the shared
    /// <c>UndoDeleteButton</c> control.
    /// </summary>
    [RelayCommand]
    private async Task FinalizeDelete()
    {
        await FlushPendingDeleteAsync();
    }

    #endregion Commands
}
