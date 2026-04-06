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
// unused — the new flow projects from ISetupStore into a separate
// `setupRows` collection that shadows the base's Items property.
public partial class SetupListViewModel : ItemListViewModelBase
{
    #region Private fields

    private readonly ISetupStore setupStore;
    private readonly ISetupCoordinator setupCoordinator;
    private readonly ImportSessionsViewModel importSessionsPage;
    private readonly ReadOnlyObservableCollection<SetupRowViewModel> setupRows;
    private readonly BehaviorSubject<Func<SetupSnapshot, bool>> filterSubject = new(_ => true);

    #endregion Private fields

    #region Observable properties

    public new ReadOnlyObservableCollection<SetupRowViewModel> Items => setupRows;

    #endregion Observable properties

    #region Constructors

    public SetupListViewModel()
    {
        setupStore = null!;
        setupCoordinator = null!;
        importSessionsPage = null!;
        setupRows = new ReadOnlyObservableCollection<SetupRowViewModel>([]);
    }

    public SetupListViewModel(
        ISetupStore setupStore,
        ISetupCoordinator setupCoordinator,
        ImportSessionsViewModel importSessionsPage)
    {
        this.setupStore = setupStore;
        this.setupCoordinator = setupCoordinator;
        this.importSessionsPage = importSessionsPage;

        setupStore.Connect()
            .Filter(filterSubject)
            .TransformWithInlineUpdate(
                snapshot => new SetupRowViewModel(snapshot, setupCoordinator),
                (row, snapshot) => row.Update(snapshot))
            .Bind(out setupRows)
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
        // The coordinator validates the suggested board id (ignores
        // it if a setup is already associated with it), so just pass
        // the currently-selected datastore's board id straight through.
        _ = setupCoordinator.OpenCreateAsync(importSessionsPage.SelectedDataStore?.BoardId);
    }

    #endregion ItemListViewModelBase overrides

    #region Commands

    [RelayCommand]
    private async Task RowSelected(SetupRowViewModel? row)
    {
        if (row is null) return;
        await setupCoordinator.OpenEditAsync(row.Id);
    }

    #endregion Commands
}
