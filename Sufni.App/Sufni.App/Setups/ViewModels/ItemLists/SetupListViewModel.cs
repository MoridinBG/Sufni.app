using Sufni.App.Setups.Queries;
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

using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Stores;
using Sufni.App.Setups.ViewModels.Rows;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;
namespace Sufni.App.Setups.ViewModels.ItemLists;

// Inherits from ItemListViewModelBase for the shared search-bar /
// date-filter / menu-item state. The items collection is owned locally
// — `setupRows` is a typed projection from the store, exposed via the
// `new` shadow on `Items`.
public partial class SetupListViewModel : ItemListViewModelBase
{
    #region Private fields

    private readonly ISetupCoordinator setupCoordinator;
    private readonly CancellableOperation importOperation = new();
    private readonly IShellCoordinator shell;
    private readonly ISetupBoardQuery setupQuery;
    private readonly ObservableCollectionExtended<SetupRowViewModel> setupRowsSource = [];
    private readonly ReadOnlyObservableCollection<SetupRowViewModel> setupRows;
    private readonly BehaviorSubject<Func<SetupDetails, bool>> filterSubject = new(_ => true);

    #endregion Private fields

    #region Observable properties

    public ReadOnlyObservableCollection<SetupRowViewModel> Items => setupRows;

    #endregion Observable properties

    #region Constructors

    public SetupListViewModel(
        ISetupBoardQuery setupQuery,
        ISetupCoordinator setupCoordinator,
        IUiThreadDispatcher uiThreadDispatcher,
        IPendingDeletes pendingDeletes,
        TimeProvider timeProvider,
        IShellCoordinator shell,
        IShellActions shellActions)
        : base(uiThreadDispatcher, timeProvider: timeProvider)
    {
        this.setupQuery = setupQuery;
        this.shell = shell;
        this.setupCoordinator = setupCoordinator;
        setupRows = new ReadOnlyObservableCollection<SetupRowViewModel>(setupRowsSource);
        ObservePendingDeletes<PendingDeleteKey.Setup>(pendingDeletes);
        MenuItems.Add(new("sync", shellActions.SyncCommand));
        MenuItems.Add(new("add", AddCommand));
        MenuItems.Add(new("import", ImportCommand));
    }

    #endregion Constructors

    #region ItemListViewModelBase overrides

    protected override void AttachSubscriptions(CompositeDisposable subscriptions)
    {
        subscriptions.Add(Disposable.Create(importOperation.Cancel));
        setupRowsSource.Clear();
        RebuildFilter();

        subscriptions.Add(setupQuery.Connect()
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
        importOperation.Cancel();
        setupRowsSource.Clear();
    }

    protected override void RebuildFilter()
    {
        var current = SearchText;
        filterSubject.OnNext(snapshot =>
            !IsPendingDelete(new PendingDeleteKey.Setup(snapshot.Setup.Id)) &&
            (string.IsNullOrEmpty(current) ||
             snapshot.Setup.Name.Contains(current, StringComparison.CurrentCultureIgnoreCase)));
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
        var snapshot = setupQuery.Get(row.Id);
        if (snapshot is null) return;
        ScheduleDelete(new PendingDeleteKey.Setup(snapshot.Setup.Id), snapshot.Setup.Name);
    }

    #endregion Private methods

    #region Commands

    [RelayCommand]
    private async Task Import()
    {
        var token = importOperation.Start();
        try
        {
            var result = await setupCoordinator.ImportSetupAsync(token);
            if (token.IsCancellationRequested) return;

            switch (result)
            {
                case SetupImportResult.Imported imported:
                    shell.OpenEditor(new EditorKey.Setup(imported.SetupId));
                    break;
                case SetupImportResult.BoardInUse:
                    ErrorMessages.Add(FailurePresentation.From(SetupValidationFailureReason.BoardInUse));
                    break;
                case SetupImportResult.InvalidFile invalid:
                    ErrorMessages.Add(new FailurePresentation(invalid.ErrorMessage));
                    break;
                case SetupImportResult.Failed failed:
                    ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                    break;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    [RelayCommand]
    private System.Threading.Tasks.Task RowSelected(SetupRowViewModel? row)
    {
        if (row is null) return System.Threading.Tasks.Task.CompletedTask;
        shell.OpenEditor(new EditorKey.Setup(row.Id));
        return System.Threading.Tasks.Task.CompletedTask;
    }

    #endregion Commands
}
