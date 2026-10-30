using Sufni.App.Shell.ViewModels;
using Sufni.App.Shell.Coordinators;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using DynamicData.Binding;
using Sufni.App.Infrastructure;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.Rows;
using Sufni.App.Shared.Base;
namespace Sufni.App.Bikes.ViewModels.ItemLists;

// Inherits from ItemListViewModelBase for the shared search-bar /
// date-filter / menu-item state. The items collection is owned locally
// — `bikeRows` is a typed projection from the store, exposed via the
// `new` shadow on `Items`.
public partial class BikeListViewModel : ItemListViewModelBase
{
    #region Private fields

    private readonly IShellCoordinator shell;
    private readonly IBikeStore bikeStore;
    private readonly IBikeDependencyQuery dependencyQuery;
    private readonly ObservableCollectionExtended<BikeRowViewModel> bikeRowsSource = [];
    private readonly ReadOnlyObservableCollection<BikeRowViewModel> bikeRows;
    private readonly BehaviorSubject<Func<BikeRowViewModel, bool>> filterSubject = new(_ => true);

    #endregion Private fields

    #region Observable properties

    public ReadOnlyObservableCollection<BikeRowViewModel> Items => bikeRows;

    #endregion Observable properties

    #region Constructors

    public BikeListViewModel(
        IBikeStore bikeStore,
        IBikeDependencyQuery dependencyQuery,
        IUiThreadDispatcher uiThreadDispatcher,
        IPendingDeletes pendingDeletes,
        TimeProvider timeProvider,
        IShellCoordinator shell,
        IShellActions shellActions)
        : base(uiThreadDispatcher, timeProvider: timeProvider)
    {
        this.bikeStore = bikeStore;
        this.shell = shell;
        this.dependencyQuery = dependencyQuery;
        bikeRows = new ReadOnlyObservableCollection<BikeRowViewModel>(bikeRowsSource);
        ObservePendingDeletes<PendingDeleteKey.Bike>(pendingDeletes);
        MenuItems.Add(new("sync", shellActions.SyncCommand));
        MenuItems.Add(new("add", AddCommand));
    }

    #endregion Constructors

    #region ItemListViewModelBase overrides

    protected override void AttachSubscriptions(CompositeDisposable subscriptions)
    {
        bikeRowsSource.Clear();
        RebuildFilter();

        // Pipeline order matters:
        //   1. Transform creates a row per snapshot.
        //   2. DisposeMany sits between Transform and Filter so it
        //      only fires when a row leaves the source store, not
        //      when the filter merely hides it.
        //   3. Filter operates on rows (so the predicate sees the
        //      same Id/Name we already exposed on the row VM).
        subscriptions.Add(bikeStore.Connect()
            .TransformWithInlineUpdate(
                snapshot => new BikeRowViewModel(snapshot, shell, RequestRowDelete, dependencyQuery),
                (row, snapshot) => row.Update(snapshot))
            .DisposeMany()
            .Filter(filterSubject)
            .Bind(bikeRowsSource)
            .Subscribe());

        PropertyChanged += OnListPropertyChanged;
        subscriptions.Add(Disposable.Create(() => PropertyChanged -= OnListPropertyChanged));
    }

    protected override void OnSubscriptionsDetached()
    {
        bikeRowsSource.Clear();
    }

    protected override void RebuildFilter()
    {
        var current = SearchText;
        filterSubject.OnNext(row =>
            !IsPendingDelete(new PendingDeleteKey.Bike(row.Id)) &&
            (string.IsNullOrEmpty(current) ||
             (row.Name?.Contains(current, StringComparison.CurrentCultureIgnoreCase) ?? false)));
    }

    protected override void AddImplementation()
    {
        shell.OpenEditor(new EditorKey.NewBike());
    }

    #endregion ItemListViewModelBase overrides

    #region Private methods

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SearchText)) RebuildFilter();
    }

    private void RequestRowDelete(BikeRowViewModel row)
    {
        var snapshot = bikeStore.Get(row.Id);
        if (snapshot is null) return;
        ScheduleDelete(new PendingDeleteKey.Bike(snapshot.Id), snapshot.Name);
    }

    #endregion Private methods

    #region Commands

    [RelayCommand]
    private System.Threading.Tasks.Task RowSelected(BikeRowViewModel? row)
    {
        if (row is null) return System.Threading.Tasks.Task.CompletedTask;
        shell.OpenEditor(new EditorKey.Bike(row.Id));
        return System.Threading.Tasks.Task.CompletedTask;
    }

    #endregion Commands
}
