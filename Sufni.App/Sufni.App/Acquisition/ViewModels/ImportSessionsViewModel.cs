using Sufni.App.Setups.Queries;
using Sufni.App.Shared.Models;
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;
using Sufni.App.Infrastructure;
using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;
namespace Sufni.App.Acquisition.ViewModels;

public partial class ImportSessionsViewModel : TabPageViewModelBase
{
    #region Observable properties

    public ObservableCollection<ITelemetryDataStore> TelemetryDataStores { get; }
    public ObservableCollection<ImportFileRowViewModel> TelemetryFiles { get; } = [];

    [ObservableProperty] public partial ITelemetryDataStore? SelectedDataStore { get; set; }
    [ObservableProperty] public partial bool NewDataStoresAvailable { get; set; }
    [ObservableProperty] public partial bool IsLoadingFiles { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportSessionsCommand))]
    public partial bool IsImporting { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportSessionsCommand))]
    public partial Guid? SelectedSetup { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportProgressText))]
    public partial int CurrentFileIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportProgressText))]
    public partial int TotalFiles { get; set; }

    public string ImportProgressText =>
        TotalFiles > 0 ? $"File {CurrentFileIndex}/{TotalFiles}" : string.Empty;

    #endregion Observable properties

    #region Private members

    private readonly ITelemetryDataStoreService telemetryDataStoreService;
    private readonly IFilesService filesService;
    private readonly IImportSessionsCoordinator importSessionsCoordinator;
    private readonly ISetupBoardQuery setupQuery;
    private readonly IDisposable importSubscription;
    private int displayedEventCount;

    #endregion Private members

    #region Constructors

    public ImportSessionsViewModel(
        ITelemetryDataStoreService telemetryDataStoreService,
        IFilesService filesService,
        IShellCoordinator shell,
        IDialogService dialogService,
        IImportSessionsCoordinator importSessionsCoordinator,
        ISetupBoardQuery setupQuery,
        IUiThreadDispatcher uiThreadDispatcher,
        TimeProvider timeProvider)
        : base(shell, dialogService, uiThreadDispatcher, timeProvider: timeProvider)
    {
        Name = "Import Sessions";
        this.telemetryDataStoreService = telemetryDataStoreService;
        this.filesService = filesService;
        this.importSessionsCoordinator = importSessionsCoordinator;
        this.setupQuery = setupQuery;

        importSubscription = importSessionsCoordinator.Observe()
            .ObserveOnUiThread(uiThreadDispatcher)
            .Subscribe(ApplyImportState);

        TelemetryDataStores = telemetryDataStoreService.DataStores;
        TelemetryDataStores.CollectionChanged += OnTelemetryDataStoresCollectionChanged;
        if (TelemetryDataStores.Count > 0)
        {
            SelectedDataStore = TelemetryDataStores[0];
        }
    }

    #endregion Constructors

    #region Property change handlers

    partial void OnSelectedDataStoreChanged(ITelemetryDataStore? value)
    {
        _ = HandleSelectedDataStoreChangedAsync(value);
    }

    #endregion Property change handlers

    #region Private methods

    private async Task HandleSelectedDataStoreChangedAsync(ITelemetryDataStore? value)
    {
        if (value is null)
        {
            TelemetryFiles.Clear();
            SelectedSetup = null;
            return;
        }

        ClearNewDataStoresAvailable();
        ResolveSelectedSetup();

        IsLoadingFiles = true;
        try
        {
            await LoadTelemetryFilesAsync(value);
        }
        catch (Exception e)
        {
            if (IsCurrentTelemetryFilesLoad(value))
            {
                ErrorMessages.Add(new FailurePresentation($"Error while changing data store: {e.Message}"));
            }
        }
        finally
        {
            if (IsCurrentTelemetryFilesLoad(value))
                IsLoadingFiles = false;
        }
    }

    private async Task LoadTelemetryFilesAsync(ITelemetryDataStore dataStore)
    {
        var files = await telemetryDataStoreService.LoadFilesAsync(dataStore);
        if (!IsCurrentTelemetryFilesLoad(dataStore))
            return;

        ApplyTelemetryFiles(files);
    }

    private void ApplyTelemetryFiles(IReadOnlyList<ITelemetryFile> files)
    {
        TelemetryFiles.Clear();
        foreach (var file in files)
        {
            TelemetryFiles.Add(new ImportFileRowViewModel(file));
        }
    }

    private bool IsCurrentTelemetryFilesLoad(ITelemetryDataStore originStore) =>
        ReferenceEquals(SelectedDataStore, originStore);

    private void ApplyImportState(SessionImportState state)
    {
        IsImporting = state is SessionImportState.Running;
        CurrentFileIndex = state is SessionImportState.Running running ? running.Current : 0;
        TotalFiles = state is SessionImportState.Running active ? active.Total : 0;
        if (state.Events.Count == 0) displayedEventCount = 0;
        foreach (var message in state.Events.Skip(displayedEventCount))
        {
            switch (message)
            {
                case SessionImportEvent.Imported imported:
                    Notifications.Insert(0, $"{imported.Snapshot.Name} was successfully imported.");
                    break;
                case SessionImportEvent.AlreadyImported existing:
                    Notifications.Insert(0, $"{existing.FileName} was already imported.");
                    break;
                case SessionImportEvent.AcknowledgementFailed acknowledgement:
                    var warning = FailurePresentation.From(SessionImportFailureReason.Acknowledge, acknowledgement.Details);
                    ErrorMessages.Add(new FailurePresentation($"{acknowledgement.FileName}: {warning.Summary}", warning.Details));
                    break;
                case SessionImportEvent.Failed failed:
                    var presentation = failed.Failure switch
                    {
                        SessionImportFailure.OperationFailed operation => FailurePresentation.From(operation.Reason, operation.Details),
                        SessionImportFailure.ProcessingFailed processing => FailurePresentation.From(processing.Reason, processing.Details),
                        _ => throw new ArgumentOutOfRangeException(nameof(message))
                    };
                    ErrorMessages.Add(new FailurePresentation($"{failed.Failure.FileName}: {presentation.Summary}", presentation.Details));
                    break;
            }
        }
        displayedEventCount = state.Events.Count;

        switch (state)
        {
            case SessionImportState.Completed completed:
                AddImportSummary(completed.Result);
                if (SelectedDataStore is { } dataStore) _ = RefreshAfterImportAsync(dataStore);
                break;
            case SessionImportState.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    private async Task RefreshAfterImportAsync(ITelemetryDataStore dataStore)
    {
        try
        {
            await LoadTelemetryFilesAsync(dataStore);
        }
        catch (Exception e)
        {
            if (IsCurrentTelemetryFilesLoad(dataStore)) ErrorMessages.Add(new FailurePresentation($"Import failed: {e.Message}"));
        }
    }

    private void AddImportSummary(SessionImportResult result)
    {
        var unpublishedCount = result.Failures.Count(failure =>
            failure.Operation is SessionImportFailureOperation.Publish);
        if (unpublishedCount > 0)
        {
            Notifications.Insert(
                0,
                $"Import finished: {result.Imported.Count} committed, {unpublishedCount} unpublished, {result.Failures.Count - unpublishedCount} failed.");
            return;
        }

        Notifications.Insert(
            0,
            $"Import finished: {result.Imported.Count} imported, {result.Failures.Count} failed.");
    }

    private void ResolveSelectedSetup()
    {
        var boardId = SelectedDataStore?.BoardId;
        SelectedSetup = boardId.HasValue
            ? setupQuery.FindByBoardId(boardId.Value)?.Id
            : null;
    }

    private void OnTelemetryDataStoresCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var comparer = new TelemetryDataStoreComparer();
        var removed = e.OldItems?.OfType<ITelemetryDataStore>().FirstOrDefault();

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                NewDataStoresAvailable = true;
                SelectedDataStore ??= TelemetryDataStores[0];
                break;
            case NotifyCollectionChangedAction.Remove:
                if (TelemetryDataStores.Count == 0)
                {
                    SelectedDataStore = null;
                    return;
                }

                if (removed is not null && comparer.Equals(SelectedDataStore, removed))
                {
                    SelectedDataStore = TelemetryDataStores[^1];
                }
                break;
            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Reset:
                break;
        }
    }

    [RelayCommand]
    private async Task OpenDataStore()
    {
        var folder = await filesService.OpenDataStoreFolderAsync();
        if (folder is null) return;

        try
        {
            var result = await telemetryDataStoreService.TryAddStorageProviderAsync(folder);
            switch (result)
            {
                case StorageProviderRegistrationResult.Added added:
                    SelectedDataStore = added.DataStore;
                    break;
                case StorageProviderRegistrationResult.AlreadyOpen alreadyOpen:
                    Notifications.Add("Folder is already opened.");
                    SelectedDataStore = alreadyOpen.DataStore;
                    break;
            }
        }
        catch (Exception e)
        {
            ErrorMessages.Add(new FailurePresentation($"Could not open folder: {e.Message}"));
        }
    }

    [RelayCommand(CanExecute = nameof(CanImportSessions))]
    private async Task ImportSessions()
    {
        if (SelectedDataStore is null || SelectedSetup is not { } setupId)
            return;

        await importSessionsCoordinator.ImportAsync(SelectedDataStore, TelemetryFiles.Select(row => row.ToImportItem()).ToArray(), setupId);
    }

    private bool CanImportSessions() => SelectedSetup != null && !IsImporting;

    [RelayCommand]
    private void ShowMalformedMessage(ImportFileRowViewModel? file)
    {
        if (file is null || string.IsNullOrWhiteSpace(file.MalformedMessage))
            return;

        var message = $"{file.FileName} is malformed: {file.MalformedMessage}";
        if (!Notifications.Contains(message))
        {
            Notifications.Add(message);
        }
    }

    #endregion Private methods

    #region Commands

    [RelayCommand]
    private void ClearNewDataStoresAvailable()
    {
        NewDataStoresAvailable = false;
    }

    [RelayCommand]
    private Task AddSetup()
    {
        shell.OpenEditor(new EditorKey.NewSetup(SelectedDataStore?.BoardId));
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void Loaded()
    {
        telemetryDataStoreService.DiscoveryIssueReported += OnDiscoveryIssueReported;
        telemetryDataStoreService.StartBrowse();
        ResolveSelectedSetup();

        EnsureScopedSubscription(s =>
        {
            var changes = setupQuery.Changes.ObserveOnUiThread(UiThreadDispatcher);
            s.Add(changes.Subscribe(_ => ResolveSelectedSetup()));
        });
    }

    [RelayCommand]
    private void Unloaded()
    {
        DisposeScopedSubscriptions();
        SelectedDataStore = null;
        SelectedSetup = null;
        NewDataStoresAvailable = false;
        TelemetryFiles.Clear();
        telemetryDataStoreService.DiscoveryIssueReported -= OnDiscoveryIssueReported;
        telemetryDataStoreService.StopBrowse();
    }

    protected override Task CloseImplementation()
    {
        importSubscription.Dispose();
        TelemetryDataStores.CollectionChanged -= OnTelemetryDataStoresCollectionChanged;
        Unloaded();
        return Task.CompletedTask;
    }

    private void OnDiscoveryIssueReported(object? sender, DataStoreDiscoveryIssue issue)
    {
        switch (issue)
        {
            case DataStoreDiscoveryIssue.DaqUnavailable:
                Notifications.Add("The DAQ is unavailable. Check that it is connected and not in use.");
                break;
            case DataStoreDiscoveryIssue.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    #endregion Commands
}
