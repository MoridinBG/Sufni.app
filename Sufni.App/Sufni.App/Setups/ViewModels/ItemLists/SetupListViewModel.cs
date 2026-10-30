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

    private readonly IShellCoordinator shell;
    private readonly ISetupStore setupStore;
    private readonly ObservableCollectionExtended<SetupRowViewModel> setupRowsSource = [];
    private readonly ReadOnlyObservableCollection<SetupRowViewModel> setupRows;
    private readonly BehaviorSubject<Func<SetupSnapshot, bool>> filterSubject = new(_ => true);

    #endregion Private fields

    #region Observable properties

    public ReadOnlyObservableCollection<SetupRowViewModel> Items => setupRows;

    #endregion Observable properties

    #region Constructors

    public SetupListViewModel(
        ISetupStore setupStore,
        IUiThreadDispatcher uiThreadDispatcher,
        IPendingDeletes pendingDeletes,
        TimeProvider timeProvider,
        IShellCoordinator shell)
        : base(uiThreadDispatcher, timeProvider: timeProvider)
    {
        this.setupStore = setupStore;
        this.shell = shell;
        setupRows = new ReadOnlyObservableCollection<SetupRowViewModel>(setupRowsSource);
        ObservePendingDeletes<PendingDeleteKey.Setup>(pendingDeletes);
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
                snapshot => new SetupRowViewModel(snapshot, shell, RequestRowDelete),
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
        filterSubject.OnNext(snapshot =>
            !IsPendingDelete(new PendingDeleteKey.Setup(snapshot.Id)) &&
            (string.IsNullOrEmpty(current) ||
             snapshot.Name.Contains(current, StringComparison.CurrentCultureIgnoreCase)));
    }

    protected override void AddImplementation()
    {
        shell.OpenEditor(new EditorKey.NewSetup());
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
        ScheduleDelete(new PendingDeleteKey.Setup(snapshot.Id), snapshot.Name);
    }

    #endregion Private methods

    #region Commands

    [RelayCommand]
    private System.Threading.Tasks.Task RowSelected(SetupRowViewModel? row)
    {
        if (row is null) return System.Threading.Tasks.Task.CompletedTask;
        shell.OpenEditor(new EditorKey.Setup(row.Id));
        return System.Threading.Tasks.Task.CompletedTask;
    }

    #endregion Commands
}
