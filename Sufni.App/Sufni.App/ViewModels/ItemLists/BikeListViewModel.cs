using System;
using System.Collections.ObjectModel;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using Sufni.App.Coordinators;
using Sufni.App.Queries;
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
    private readonly IBikeDependencyQuery dependencyQuery;
    private readonly ReadOnlyObservableCollection<BikeRowViewModel> bikeRows;
    private readonly BehaviorSubject<Func<BikeRowViewModel, bool>> filterSubject = new(_ => true);
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
        dependencyQuery = null!;
        bikeRows = new ReadOnlyObservableCollection<BikeRowViewModel>([]);
    }

    public BikeListViewModel(
        IBikeStore bikeStore,
        IBikeCoordinator bikeCoordinator,
        IBikeDependencyQuery dependencyQuery)
    {
        this.bikeStore = bikeStore;
        this.bikeCoordinator = bikeCoordinator;
        this.dependencyQuery = dependencyQuery;

        // Pipeline order matters:
        //   1. Transform creates a row per snapshot.
        //   2. DisposeMany sits between Transform and Filter so it
        //      only fires when a row leaves the source store, not
        //      when the filter merely hides it.
        //   3. Filter operates on rows (so the predicate sees the
        //      same Id/Name we already exposed on the row VM).
        bikeStore.Connect()
            .TransformWithInlineUpdate(
                snapshot => new BikeRowViewModel(snapshot, bikeCoordinator, RequestRowDelete, dependencyQuery),
                (row, snapshot) => row.Update(snapshot))
            .DisposeMany()
            .Filter(filterSubject)
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
        filterSubject.OnNext(row =>
            (pendingId is null || row.Id != pendingId) &&
            (string.IsNullOrEmpty(current) ||
             (row.Name?.Contains(current, StringComparison.CurrentCultureIgnoreCase) ?? false)));
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

    private void RequestRowDelete(BikeRowViewModel row)
    {
        _ = RunPendingDeleteInteractionAsync(async () =>
        {
            var snapshot = bikeStore.Get(row.Id);
            if (snapshot is null) return;

            // Commit any in-flight pending delete first.
            await FlushPendingDeleteAsync();

            pendingDelete = (snapshot.Id, snapshot.Name);
            RebuildFilter();

            StartUndoWindow(snapshot.Name, () => FinalizeBikeDeleteAsync(snapshot.Id));
        });
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
