using System;
using System.Collections.ObjectModel;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using Sufni.App.Coordinators;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Rows;

namespace Sufni.App.ViewModels.ItemLists;

// Inherits from ItemListViewModelBase for the shared search-bar /
// date-filter / menu-item state. The items collection is owned locally
// — `bikeRows` is a typed projection from the store, exposed via the
// `new` shadow on `Items`.
public partial class BikeListViewModel : ItemListViewModelBase
{
    #region Private fields

    private readonly IBikeStore bikeStore;
    private readonly IBikeCoordinator bikeCoordinator;
    private readonly ReadOnlyObservableCollection<BikeRowViewModel> bikeRows;
    private readonly BehaviorSubject<Func<BikeSnapshot, bool>> filterSubject = new(_ => true);
    private (Guid Id, string Name)? pendingDelete;

    #endregion Private fields

    #region Observable properties

    public ReadOnlyObservableCollection<BikeRowViewModel> Items => bikeRows;

    #endregion Observable properties

    #region Constructors

    public BikeListViewModel()
    {
        bikeStore = null!;
        bikeCoordinator = null!;
        bikeRows = new ReadOnlyObservableCollection<BikeRowViewModel>([]);
    }

    public BikeListViewModel(IBikeStore bikeStore, IBikeCoordinator bikeCoordinator)
    {
        this.bikeStore = bikeStore;
        this.bikeCoordinator = bikeCoordinator;

        bikeStore.Connect()
            .Filter(filterSubject)
            .TransformWithInlineUpdate(
                snapshot => new BikeRowViewModel(snapshot, bikeCoordinator, RequestRowDelete),
                (row, snapshot) => row.Update(snapshot))
            .Bind(out bikeRows)
            .Subscribe();

        // Push a fresh predicate to our filter subject whenever the
        // search text changes.
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SearchText)) RebuildFilter();
        };
    }

    #endregion Constructors

    #region ItemListViewModelBase overrides

    protected override void RebuildFilter()
    {
        var current = SearchText;
        var pendingId = pendingDelete?.Id;
        filterSubject.OnNext(snapshot =>
            (pendingId is null || snapshot.Id != pendingId) &&
            (string.IsNullOrEmpty(current) ||
             snapshot.Name.Contains(current, StringComparison.CurrentCultureIgnoreCase)));
    }

    protected override void OnPendingDeleteUndone()
    {
        pendingDelete = null;
        RebuildFilter();
    }

    protected override void AddImplementation()
    {
        _ = bikeCoordinator.OpenCreateAsync();
    }

    #endregion ItemListViewModelBase overrides

    #region Private methods

    private async void RequestRowDelete(BikeRowViewModel row)
    {
        var snapshot = bikeStore.Get(row.Id);
        if (snapshot is null) return;

        // Commit any in-flight pending delete first.
        await FlushPendingDeleteAsync();

        pendingDelete = (snapshot.Id, snapshot.Name);
        RebuildFilter();

        StartUndoWindow(snapshot.Name, () => FinalizeBikeDeleteAsync(snapshot.Id));
    }

    private async Task FinalizeBikeDeleteAsync(Guid bikeId)
    {
        pendingDelete = null;
        RebuildFilter();

        var result = await bikeCoordinator.DeleteAsync(bikeId);
        switch (result.Outcome)
        {
            case BikeDeleteOutcome.InUse:
                ErrorMessages.Add("Bike is referenced by a setup and cannot be deleted.");
                break;
            case BikeDeleteOutcome.Failed:
                ErrorMessages.Add($"Bike could not be deleted: {result.ErrorMessage}");
                break;
        }
    }

    #endregion Private methods

    #region Commands

    [RelayCommand]
    private async Task RowSelected(BikeRowViewModel? row)
    {
        if (row is null) return;
        await bikeCoordinator.OpenEditAsync(row.Id);
    }

    #endregion Commands
}
