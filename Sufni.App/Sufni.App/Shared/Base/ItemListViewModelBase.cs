using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using Sufni.App.Shell.Coordinators;
using System.Reactive.Disposables;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Sufni.App.Shell.ViewModels;
using Sufni.App.Infrastructure;
namespace Sufni.App.Shared.Base;

/// <summary>Shared list filtering, menu state, and pending-delete presentation.</summary>
public partial class ItemListViewModelBase : ViewModelBase, IDisposable
{
    #region Observable properties

    [ObservableProperty] public partial string? SearchText { get; set; }
    [ObservableProperty] public partial bool SearchBoxIsFocused { get; set; }
    [ObservableProperty] public partial DateTime? DateFilterFrom { get; set; }
    [ObservableProperty] public partial DateTime? DateFilterTo { get; set; }
    [ObservableProperty] public partial bool DateFilterVisible { get; set; }

    public ObservableCollection<PullMenuItemViewModel> MenuItems { get; set; } = [];

    public ObservableCollection<PendingDeleteEntryViewModel> PendingDeletes { get; } = [];

    private IPendingDeletes? pendingDeleteOwner;
    private readonly CompositeDisposable pendingDeleteSubscriptions = new();

    #endregion Observable properties

    public ItemListViewModelBase(
        IUiThreadDispatcher uiThreadDispatcher,
        TimeProvider timeProvider)
        : base(uiThreadDispatcher, timeProvider: timeProvider)
    {
    }

    #region Property change handlers

    partial void OnSearchBoxIsFocusedChanged(bool value)
    {
        DateFilterVisible = value || HasActiveDateFilter();
    }

    partial void OnDateFilterFromChanged(DateTime? value)
    {
        DateFilterVisible = SearchBoxIsFocused || HasActiveDateFilter();
    }

    partial void OnDateFilterToChanged(DateTime? value)
    {
        DateFilterVisible = SearchBoxIsFocused || HasActiveDateFilter();
    }

    private bool HasActiveDateFilter() => DateFilterFrom is not null || DateFilterTo is not null;

    #endregion Property change handlers

    #region Virtual methods

    protected virtual void AddImplementation() { }

    protected virtual void AttachSubscriptions(CompositeDisposable subscriptions) { }

    protected virtual void OnSubscriptionsDetached() { }

    /// <summary>
    /// Republish the filter predicate so the row hide/reveal triggered
    /// by a pending delete or undo is observed by the DynamicData chain.
    /// Subclasses must override.
    /// </summary>
    protected virtual void RebuildFilter() { }

    #endregion Virtual methods

    protected void ObservePendingDeletes<TKey>(IPendingDeletes owner) where TKey : PendingDeleteKey
    {
        pendingDeleteOwner = owner;
        pendingDeleteSubscriptions.Add(owner.Observe().Subscribe(values =>
        {
            var previous = PendingDeletes.ToDictionary(entry => entry.Key);
            PendingDeletes.Clear();
            foreach (var value in values.Where(value => value.Key is TKey))
                PendingDeletes.Add(previous.TryGetValue(value.Key, out var entry)
                    ? entry : new PendingDeleteEntryViewModel(value, owner));
            RebuildFilter();
        }));
        EventHandler<PendingDeleteCompletion> completed = (_, result) =>
        {
            if (result.Key is TKey && result.Error is { } error) ErrorMessages.Add(error);
        };
        owner.Completed += completed;
        pendingDeleteSubscriptions.Add(Disposable.Create(() => owner.Completed -= completed));
    }

    protected bool IsPendingDelete(PendingDeleteKey key) => pendingDeleteOwner!.IsPending(key);
    protected void ScheduleDelete(PendingDeleteKey key, string name) => pendingDeleteOwner!.Schedule(key, name);

    public void Dispose()
    {
        DisposeScopedSubscriptions();
        pendingDeleteSubscriptions.Dispose();
    }

    #region Commands

    [RelayCommand]
    private void Add()
    {
        AddImplementation();
    }

    [RelayCommand]
    private void Loaded()
    {
        EnsureScopedSubscription(AttachSubscriptions);
    }

    [RelayCommand]
    private void Unloaded()
    {
        DisposeScopedSubscriptions();
        OnSubscriptionsDetached();
    }

    [RelayCommand]
    private void ClearSearchText()
    {
        SearchText = null;
        DateFilterVisible = HasActiveDateFilter();
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
