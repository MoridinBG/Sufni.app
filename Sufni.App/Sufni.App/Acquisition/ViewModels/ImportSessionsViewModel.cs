using Sufni.App.Shell.ViewModels.Base;
using Sufni.App.Setups.Queries;
using Sufni.App.LiveDaq.ViewModels;
using Sufni.App.LiveDaq.Services.LiveStreaming;
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
using Sufni.App.Shared.Common;
using Sufni.App.Shell.Coordinators;
namespace Sufni.App.Acquisition.ViewModels;

public partial class ImportSessionsViewModel : TabPageViewModelBase
{
    #region Observable properties

    public ReadOnlyObservableCollection<ITelemetryDataStore> TelemetryDataStores { get; }
    public ObservableCollection<ImportFileRowViewModel> TelemetryFiles { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportSessionsCommand))]
    public partial ITelemetryDataStore? SelectedDataStore { get; set; }
    [ObservableProperty] public partial bool NewDataStoresAvailable { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportSessionsCommand))]
    public partial bool IsLoadingFiles { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportSessionsCommand))]
    [NotifyPropertyChangedFor(nameof(IsSourceBusy))]
    [NotifyPropertyChangedFor(nameof(SourceBusyMessage))]
    public partial DaqTransportHolder? SourceHolder { get; private set; }

    public bool HasDataStores => TelemetryDataStores.Count > 0;
    public bool IsSourceBusy => SourceHolder is not null;
    public string SourceBusyMessage => SourceHolder is { } holder ? LiveDaqMessages.Busy(holder) : string.Empty;

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

    private readonly IImportSessionsCoordinator importSessionsCoordinator;
    private readonly ISetupBoardQuery setupQuery;
    private readonly IDisposable importSubscription;
    private int displayedEventCount;
    private IDisposable? sourceObservation;
    private IDisposable? sourceTransportObservation;
    private readonly CancellableOperation listingOperation = new();
    private Task pendingListings = Task.CompletedTask;
    public bool CanPickFolder => importSessionsCoordinator.CanPickFolder;
    private TelemetryFileListing.Listed? loadedListing;
    private Guid? SelectedBoardId => loadedListing?.BoardId ?? SelectedDataStore?.BoardId;

    #endregion Private members

    #region Constructors

    public ImportSessionsViewModel(
        IShellCoordinator shell,
        IDialogService dialogService,
        IImportSessionsCoordinator importSessionsCoordinator,
        ISetupBoardQuery setupQuery,
        IUiThreadDispatcher uiThreadDispatcher,
        TimeProvider timeProvider)
        : base(shell, dialogService, uiThreadDispatcher, timeProvider: timeProvider)
    {
        Name = "Import Sessions";
        this.importSessionsCoordinator = importSessionsCoordinator;
        this.setupQuery = setupQuery;

        importSubscription = importSessionsCoordinator.Observe()
            .ObserveOnUiThread(uiThreadDispatcher)
            .Subscribe(ApplyImportState);

        TelemetryDataStores = importSessionsCoordinator.Sources;
        ((INotifyCollectionChanged)TelemetryDataStores).CollectionChanged += OnTelemetryDataStoresCollectionChanged;
        if (TelemetryDataStores.Count > 0)
        {
            SelectedDataStore = TelemetryDataStores[0];
        }
    }

    #endregion Constructors

    #region Property change handlers

    partial void OnSelectedDataStoreChanged(ITelemetryDataStore? value)
    {
        sourceTransportObservation?.Dispose();
        sourceTransportObservation = null;
        SourceHolder = null;
        listingOperation.Cancel();
        loadedListing = null;
        TelemetryFiles.Clear();
        IsLoadingFiles = false;
        if (value is null)
        {
            SelectedSetup = null;
            return;
        }
        sourceTransportObservation = value.ObserveTransport().ObserveOnUiThread(UiThreadDispatcher)
            .Subscribe(holder => SourceHolder = holder);
        ClearNewDataStoresAvailable();
        ResolveSelectedSetup();
        RequestFileLoad(value);
    }

    #endregion Property change handlers

    #region Private methods

    private void RequestFileLoad(ITelemetryDataStore source)
    {
        var next = LoadTelemetryFilesAsync(source);
        pendingListings = pendingListings.IsCompleted ? next : Task.WhenAll(pendingListings, next);
    }

    private async Task LoadTelemetryFilesAsync(ITelemetryDataStore dataStore)
    {
        var cancellationToken = listingOperation.Start();
        IsLoadingFiles = true;
        try
        {
            var listing = await importSessionsCoordinator.LoadFilesAsync(dataStore, cancellationToken);
            if (cancellationToken.IsCancellationRequested || !IsCurrentTelemetryFilesLoad(dataStore)) return;
            switch (listing)
            {
                case TelemetryFileListing.Listed listed:
                    loadedListing = listed;
                    ApplyTelemetryFiles(listed.Files);
                    if (SelectedSetup is null) ResolveSelectedSetup();
                    break;
                case TelemetryFileListing.Busy busy:
                    Notifications.Add(LiveDaqMessages.Busy(busy.Holder));
                    break;
                case TelemetryFileListing.Unavailable:
                    Notifications.Add("The DAQ is unavailable. Try again when it is connected.");
                    break;
                case TelemetryFileListing.Failed failed:
                    ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                    break;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!cancellationToken.IsCancellationRequested && IsCurrentTelemetryFilesLoad(dataStore)) IsLoadingFiles = false;
        }
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
                if (SelectedDataStore is { } dataStore) RequestFileLoad(dataStore);
                break;
            case SessionImportState.SourceBusy busy:
                Notifications.Add(LiveDaqMessages.Busy(busy.Holder));
                break;
            case SessionImportState.SourceUnavailable:
                Notifications.Add("The DAQ is unavailable. Try again when it is connected.");
                break;
            case SessionImportState.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
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
        var boardId = SelectedBoardId;
        SelectedSetup = boardId.HasValue
            ? setupQuery.FindByBoardId(boardId.Value)?.Id
            : null;
    }

    private void OnTelemetryDataStoresCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasDataStores));
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
                if (ReferenceEquals(SelectedDataStore, removed)) SelectedDataStore = e.NewItems?.OfType<ITelemetryDataStore>().FirstOrDefault();
                break;
            case NotifyCollectionChangedAction.Move:
                break;
            case NotifyCollectionChangedAction.Reset:
                if (SelectedDataStore is null || !TelemetryDataStores.Contains(SelectedDataStore))
                    SelectedDataStore = TelemetryDataStores.FirstOrDefault();
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPickFolder))]
    private async Task OpenDataStore(CancellationToken cancellationToken)
    {
        try
        {
            switch (await importSessionsCoordinator.OpenFolderAsync(cancellationToken))
            {
                case StorageProviderRegistrationResult.Added added:
                    SelectedDataStore = added.DataStore;
                    break;
                case StorageProviderRegistrationResult.AlreadyOpen alreadyOpen:
                    Notifications.Add("Folder is already opened.");
                    SelectedDataStore = alreadyOpen.DataStore;
                    break;
                case StorageProviderRegistrationResult.Failed failed:
                    ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                    break;
                case StorageProviderRegistrationResult.Unavailable:
                    Notifications.Add("Folder picking is unavailable.");
                    break;
            }
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand(CanExecute = nameof(CanImportSessions))]
    private async Task ImportSessions()
    {
        if (SelectedDataStore is null || SelectedSetup is not { } setupId)
            return;

        await importSessionsCoordinator.ImportAsync(SelectedDataStore, TelemetryFiles.Select(row => row.ToImportItem()).ToArray(), setupId);
    }

    private bool CanImportSessions() => SelectedDataStore is not null && SelectedSetup is not null
        && !IsImporting && !IsLoadingFiles && !IsSourceBusy;

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
        shell.OpenEditor(new EditorKey.NewSetup(SelectedBoardId));
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void Loaded()
    {
        sourceObservation ??= importSessionsCoordinator.ObserveSources(OnDiscoveryIssueReported);
        SelectedDataStore ??= TelemetryDataStores.FirstOrDefault();
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
        OpenDataStoreCommand.Cancel();
        sourceObservation?.Dispose();
        sourceObservation = null;
    }

    protected override async Task CloseImplementation()
    {
        importSubscription.Dispose();
        ((INotifyCollectionChanged)TelemetryDataStores).CollectionChanged -= OnTelemetryDataStoresCollectionChanged;
        Unloaded();
        listingOperation.Dispose();
        await pendingListings;
        if (OpenDataStoreCommand.ExecutionTask is { } folder) await folder;
    }

    private void OnDiscoveryIssueReported(DataStoreDiscoveryIssue issue) =>
        ErrorMessages.Add(FailurePresentation.From(issue.Reason, issue.Details));

    #endregion Commands
}
