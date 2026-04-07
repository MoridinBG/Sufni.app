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

    private readonly IBikeCoordinator bikeCoordinator;
    private readonly ReadOnlyObservableCollection<BikeRowViewModel> bikeRows;
    private readonly BehaviorSubject<Func<BikeSnapshot, bool>> filterSubject = new(_ => true);

    #endregion Private fields

    #region Observable properties

    public ReadOnlyObservableCollection<BikeRowViewModel> Items => bikeRows;

    #endregion Observable properties

    #region Constructors

    public BikeListViewModel()
    {
        bikeCoordinator = null!;
        bikeRows = new ReadOnlyObservableCollection<BikeRowViewModel>([]);
    }

    public BikeListViewModel(IBikeStore bikeStore, IBikeCoordinator bikeCoordinator)
    {
        this.bikeCoordinator = bikeCoordinator;

        bikeStore.Connect()
            .Filter(filterSubject)
            .TransformWithInlineUpdate(
                snapshot => new BikeRowViewModel(snapshot, bikeCoordinator),
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

    #region Private methods

    private void RebuildFilter()
    {
        var current = SearchText;
        filterSubject.OnNext(snapshot =>
            string.IsNullOrEmpty(current) ||
            snapshot.Name.Contains(current, StringComparison.CurrentCultureIgnoreCase));
    }

    #endregion Private methods

    #region ItemListViewModelBase overrides

    protected override void AddImplementation()
    {
        _ = bikeCoordinator.OpenCreateAsync();
    }

    #endregion ItemListViewModelBase overrides

    #region Commands

    [RelayCommand]
    private async Task RowSelected(BikeRowViewModel? row)
    {
        if (row is null) return;
        await bikeCoordinator.OpenEditAsync(row.Id);
    }

    #endregion Commands
}
