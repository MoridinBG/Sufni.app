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
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Stores;
using Sufni.App.Setups.ViewModels.Rows;
using Sufni.App.Shared.Base;
namespace Sufni.App.Setups.ViewModels.ItemLists;

// Inherits from ItemListViewModelBase for the shared search-bar /
// date-filter / menu-item state. The items collection is owned locally
// — `setupRows` is a typed projection from the store, exposed via the
// `new` shadow on `Items`.
public partial class SetupListViewModel : ItemListViewModelBase
{
    #region Private fields

    private readonly ISetupStore setupStore;
    private readonly ISetupCoordinator setupCoordinator;
    private readonly ObservableCollectionExtended<SetupRowViewModel> setupRowsSource = [];
    private readonly ReadOnlyObservableCollection<SetupRowViewModel> setupRows;
    private readonly BehaviorSubject<Func<SetupSnapshot, bool>> filterSubject = new(_ => true);
    private readonly HashSet<Guid> pendingDeleteIds = [];

    #endregion Private fields

    #region Observable properties

    public ReadOnlyObservableCollection<SetupRowViewModel> Items => setupRows;

    #endregion Observable properties

    #region Constructors

    public SetupListViewModel(
        ISetupStore setupStore,
        ISetupCoordinator setupCoordinator,
        IUiThreadDispatcher uiThreadDispatcher,
        IBackgroundTaskRunner? backgroundTaskRunner = null)
        : base(uiThreadDispatcher, backgroundTaskRunner)
    {
        this.setupStore = setupStore;
        this.setupCoordinator = setupCoordinator;
        setupRows = new ReadOnlyObservableCollection<SetupRowViewModel>(setupRowsSource);
    }

    #endregion Constructors

    #region ItemListViewModelBase overrides

    protected override void AttachSubscriptions(CompositeDisposable subscriptions)
    {
        setupRowsSource.Clear();
        RebuildFilter();

        subscriptions.Add(setupStore.Connect()
            .Filter(filterSubject)
            .TransformWithInlineUpdate(
                snapshot => new SetupRowViewModel(snapshot, setupCoordinator, RequestRowDelete),
                (row, snapshot) => row.Update(snapshot))
            .Bind(setupRowsSource)
            .Subscribe());

        PropertyChanged += OnListPropertyChanged;
        subscriptions.Add(Disposable.Create(() => PropertyChanged -= OnListPropertyChanged));
    }

    protected override void OnSubscriptionsDetached()
    {
        setupRowsSource.Clear();
    }

    protected override void RebuildFilter()
    {
        var current = SearchText;
        var pendingIds = pendingDeleteIds.Count == 0 ? null : new HashSet<Guid>(pendingDeleteIds);
        filterSubject.OnNext(snapshot =>
            (pendingIds is null || !pendingIds.Contains(snapshot.Id)) &&
            (string.IsNullOrEmpty(current) ||
             snapshot.Name.Contains(current, StringComparison.CurrentCultureIgnoreCase)));
    }

    protected override void AddImplementation()
    {
        _ = setupCoordinator.OpenCreateAsync();
    }

    #endregion ItemListViewModelBase overrides

    #region Private methods

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SearchText)) RebuildFilter();
    }

    private void RequestRowDelete(SetupRowViewModel row)
    {
        var snapshot = setupStore.Get(row.Id);
        if (snapshot is null) return;

        pendingDeleteIds.Add(snapshot.Id);
        RebuildFilter();

        StartUndoWindow(
            snapshot.Name,
            finalize: () => FinalizeSetupDeleteAsync(snapshot.Id),
            onUndone: () => OnSetupDeleteUndone(snapshot.Id));
    }

    private void OnSetupDeleteUndone(Guid setupId)
    {
        pendingDeleteIds.Remove(setupId);
        RebuildFilter();
    }

    private async Task FinalizeSetupDeleteAsync(Guid setupId)
    {
        var result = await setupCoordinator.DeleteAsync(setupId);

        pendingDeleteIds.Remove(setupId);
        RebuildFilter();

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
