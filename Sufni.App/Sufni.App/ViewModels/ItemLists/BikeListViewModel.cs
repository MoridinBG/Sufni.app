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

// Inherits from ItemListViewModelBase so the shared SearchBar and
// UndoDeleteButton (which still bind against ItemListViewModelBase) keep
// working without changes. The base's SourceCache<ItemViewModelBase> is
// unused — the new flow projects from IBikeStore into a separate
// `bikeRows` collection that shadows the base's Items property.
public partial class BikeListViewModel : ItemListViewModelBase
{
    #region Private fields

    private readonly IBikeCoordinator bikeCoordinator;
    private readonly ReadOnlyObservableCollection<BikeRowViewModel> bikeRows;
    private readonly BehaviorSubject<Func<BikeSnapshot, bool>> filterSubject = new(_ => true);

    #endregion Private fields

    #region Observable properties

    public new ReadOnlyObservableCollection<BikeRowViewModel> Items => bikeRows;

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

        // The base's OnSearchTextChanged partial method only refreshes
        // the (empty) base SourceCache. We need to refresh our own
        // filter subject when the user types.
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

    public override Task LoadFromDatabase() => Task.CompletedTask;

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
