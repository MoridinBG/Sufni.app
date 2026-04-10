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
// — `setupRows` is a typed projection from the store, exposed via the
// `new` shadow on `Items`.
public partial class SetupListViewModel : ItemListViewModelBase
{
    #region Private fields

    private readonly ISetupStore setupStore;
    private readonly ISetupCoordinator setupCoordinator;
    private readonly ImportSessionsViewModel importSessionsPage;
    private readonly ReadOnlyObservableCollection<SetupRowViewModel> setupRows;
    private readonly BehaviorSubject<Func<SetupSnapshot, bool>> filterSubject = new(_ => true);
    private (Guid Id, string Name)? pendingDelete;

    #endregion Private fields

    #region Observable properties

    public ReadOnlyObservableCollection<SetupRowViewModel> Items => setupRows;

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
                snapshot => new SetupRowViewModel(snapshot, setupCoordinator, RequestRowDelete),
                (row, snapshot) => row.Update(snapshot))
            .Bind(out setupRows)
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
        // The coordinator validates the suggested board id (ignores
        // it if a setup is already associated with it), so just pass
        // the currently-selected datastore's board id straight through.
        _ = setupCoordinator.OpenCreateAsync(importSessionsPage.SelectedDataStore?.BoardId);
    }

    #endregion ItemListViewModelBase overrides

    #region Private methods

    private async void RequestRowDelete(SetupRowViewModel row)
    {
        var snapshot = setupStore.Get(row.Id);
        if (snapshot is null) return;

        await FlushPendingDeleteAsync();

        pendingDelete = (snapshot.Id, snapshot.Name);
        RebuildFilter();

        StartUndoWindow(snapshot.Name, () => FinalizeSetupDeleteAsync(snapshot.Id));
    }

    private async Task FinalizeSetupDeleteAsync(Guid setupId)
    {
        pendingDelete = null;
        RebuildFilter();

        var result = await setupCoordinator.DeleteAsync(setupId);
        if (result.Outcome == SetupDeleteOutcome.Failed)
        {
            ErrorMessages.Add($"Setup could not be deleted: {result.ErrorMessage}");
        }
    }

    #endregion Private methods

    #region Commands

    [RelayCommand]
    private async Task RowSelected(SetupRowViewModel? row)
    {
        if (row is null) return;
        await setupCoordinator.OpenEditAsync(row.Id);
    }

    #endregion Commands
}
