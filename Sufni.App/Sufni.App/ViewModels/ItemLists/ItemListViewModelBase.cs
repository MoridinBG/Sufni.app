using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Services;

namespace Sufni.App.ViewModels.ItemLists;

/// <summary>
/// Shared search-bar / date-filter / menu-item state for the entity
/// list view models. Every concrete list owns its own store-backed
/// projection; the base contributes only the cross-cutting filter state
/// and the menu-item collection.
///
/// The base also owns the pending-delete UX (5-second undo window).
/// Each row deletion produces its own <see cref="PendingDeleteEntryViewModel"/>
/// in <see cref="PendingDeletes"/>; the entries stack independently and
/// each runs its own timer. Subclasses install their own pending state,
/// then call <see cref="StartUndoWindow"/> with a finalize callback that
/// persists the delete and an onUndone callback that restores the row.
/// </summary>
public partial class ItemListViewModelBase : ViewModelBase
{
    #region Constants

    private const int PendingDeleteWindowMs = 5000;

    #endregion Constants

    #region Observable properties

    [ObservableProperty] private string? searchText;
    [ObservableProperty] private bool searchBoxIsFocused;
    [ObservableProperty] private DateTime? dateFilterFrom;
    [ObservableProperty] private DateTime? dateFilterTo;
    [ObservableProperty] private bool dateFilterVisible;

    public ObservableCollection<PullMenuItemViewModel> MenuItems { get; set; } = [];

    public ObservableCollection<PendingDeleteEntryViewModel> PendingDeletes { get; } = [];

    #endregion Observable properties

    public ItemListViewModelBase(IUiThreadDispatcher uiThreadDispatcher)
        : base(uiThreadDispatcher)
    {
    }

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

    protected async Task RunActionSwallowExceptionToErrorMessages(Func<Task> action)
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
    /// Start a fresh 5-second undo window for one row. The supplied
    /// <paramref name="finalize"/> runs on timer expiry or when the
    /// entry's dismiss button is tapped; <paramref name="onUndone"/>
    /// runs when the entry's undo button is tapped. Caller is
    /// responsible for installing the row's pending state and calling
    /// <see cref="RebuildFilter"/> before invoking this.
    /// </summary>
    protected void StartUndoWindow(string displayName, Func<Task> finalize, Action onUndone)
    {
        var entry = new PendingDeleteEntryViewModel(
            displayName,
            finalize: async () =>
            {
                try
                {
                    await finalize();
                }
                catch (Exception exception)
                {
                    ErrorMessages.Add(exception.Message);
                }
            },
            onUndone: onUndone,
            remove: e => PendingDeletes.Remove(e));

        PendingDeletes.Add(entry);
        entry.StartTimer(PendingDeleteWindowMs);
    }

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

    #endregion Commands
}
