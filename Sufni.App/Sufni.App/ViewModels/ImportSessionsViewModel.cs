using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.ViewModels;

public partial class ImportSessionsViewModel : TabPageViewModelBase
{
    #region Observable properties

    public ObservableCollection<ITelemetryDataStore> TelemetryDataStores { get; }
    public ObservableCollection<ITelemetryFile> TelemetryFiles { get; } = [];

    [ObservableProperty] private ITelemetryDataStore? selectedDataStore;
    [ObservableProperty] private bool newDataStoresAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportSessionsCommand))]
    private Guid? selectedSetup;

    #endregion Observable properties

    #region Private members

    private readonly ITelemetryDataStoreService telemetryDataStoreService;
    private readonly IFilesService filesService;
    private readonly SetupCoordinator setupCoordinator;
    private readonly ImportSessionsCoordinator importSessionsCoordinator;
    private readonly ISetupStore setupStore;

    #endregion Private members

    #region Constructors

    public ImportSessionsViewModel(
        ITelemetryDataStoreService telemetryDataStoreService,
        IFilesService filesService,
        IShellCoordinator shell,
        IDialogService dialogService,
        SetupCoordinator setupCoordinator,
        ImportSessionsCoordinator importSessionsCoordinator,
        ISetupStore setupStore)
        : base(shell, dialogService)
    {
        Name = "Import Sessions";
        this.telemetryDataStoreService = telemetryDataStoreService;
        this.filesService = filesService;
        this.setupCoordinator = setupCoordinator;
        this.importSessionsCoordinator = importSessionsCoordinator;
        this.setupStore = setupStore;

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

        try
        {
            await LoadTelemetryFilesAsync(value);
        }
        catch (Exception e)
        {
            if (IsCurrentTelemetryFilesLoad(value))
            {
                ErrorMessages.Add($"Error while changing data store: {e.Message}");
            }
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
            TelemetryFiles.Add(file);
        }
    }

    private bool IsCurrentTelemetryFilesLoad(ITelemetryDataStore originStore) =>
        ReferenceEquals(SelectedDataStore, originStore);

    private Progress<SessionImportEvent> CreateImportProgress() =>
        new(evt =>
        {
            switch (evt)
            {
                case SessionImportEvent.Imported imported:
                    Notifications.Insert(0, $"{imported.Snapshot.Name} was successfully imported.");
                    break;
                case SessionImportEvent.Failed failed:
                    ErrorMessages.Add($"Could not import {failed.FileName}: {failed.ErrorMessage}");
                    break;
            }
        });

    private void AddImportSummary(SessionImportResult result)
    {
        Notifications.Insert(
            0,
            $"Import finished: {result.Imported.Count} imported, {result.Failures.Count} failed.");
    }

    private void ResolveSelectedSetup()
    {
        var boardId = SelectedDataStore?.BoardId;
        SelectedSetup = boardId.HasValue
            ? setupStore.FindByBoardId(boardId.Value)?.Id
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
            ErrorMessages.Add($"Could not open folder: {e.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanImportSessions))]
    private async Task ImportSessions()
    {
        if (SelectedDataStore is not { } dataStore || SelectedSetup is not { } setupId)
            return;

        var files = TelemetryFiles.ToList();

        try
        {
            var progress = CreateImportProgress();
            var result = await importSessionsCoordinator.ImportAsync(
                files,
                setupId,
                progress);
            AddImportSummary(result);

            await LoadTelemetryFilesAsync(dataStore);
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Import failed: {e.Message}");
        }
    }

    private bool CanImportSessions() => SelectedSetup != null;

    [RelayCommand]
    private void ShowMalformedMessage(ITelemetryFile? file)
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
    private async Task AddSetup() => await setupCoordinator.OpenCreateAsync(SelectedDataStore?.BoardId);

    [RelayCommand]
    private void Loaded()
    {
        telemetryDataStoreService.ErrorOccurred += OnDataStoreServiceError;
        telemetryDataStoreService.StartBrowse();
        ResolveSelectedSetup();

        EnsureScopedSubscription(s =>
        {
            var changes = SynchronizationContext.Current is { } synchronizationContext
                ? setupStore.Connect().ObserveOn(synchronizationContext)
                : setupStore.Connect();
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
        telemetryDataStoreService.ErrorOccurred -= OnDataStoreServiceError;
        telemetryDataStoreService.StopBrowse();
    }

    private void OnDataStoreServiceError(object? sender, string message)
    {
        ErrorMessages.Add(message);
    }

    #endregion Commands
}