using Sufni.App.ExtensionHost.Contracts.Services;
﻿using System;
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
    public ObservableCollection<ITelemetryFile> TelemetryFiles { get; } = [];

    [ObservableProperty] public partial ITelemetryDataStore? SelectedDataStore { get; set; }
    [ObservableProperty] public partial bool NewDataStoresAvailable { get; set; }
    [ObservableProperty] public partial bool IsLoadingFiles { get; set; }

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
    private readonly ISetupCoordinator setupCoordinator;
    private readonly IImportSessionsCoordinator importSessionsCoordinator;
    private readonly ISetupStore setupStore;

    #endregion Private members

    #region Constructors

    public ImportSessionsViewModel(
        ITelemetryDataStoreService telemetryDataStoreService,
        IFilesService filesService,
        IShellCoordinator shell,
        IDialogService dialogService,
        ISetupCoordinator setupCoordinator,
        IImportSessionsCoordinator importSessionsCoordinator,
        ISetupStore setupStore,
        IUiThreadDispatcher uiThreadDispatcher)
        : base(shell, dialogService, uiThreadDispatcher)
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

        IsLoadingFiles = true;
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
                case SessionImportEvent.ImportFailed failed:
                    ErrorMessages.Add($"Could not import {failed.FileName}: {failed.ErrorMessage}");
                    break;
                case SessionImportEvent.PublicationFailed failed:
                    ErrorMessages.Add(
                        $"{failed.FileName} was imported, but app state could not be refreshed; the source was left unacknowledged: {failed.ErrorMessage}");
                    break;
                case SessionImportEvent.TrashFailed failed:
                    ErrorMessages.Add($"Could not trash {failed.FileName}: {failed.ErrorMessage}");
                    break;
                case SessionImportEvent.Progress progressEvent:
                    CurrentFileIndex = progressEvent.Current;
                    TotalFiles = progressEvent.Total;
                    break;
            }
        });

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

        CurrentFileIndex = 0;
        TotalFiles = 0;

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
        finally
        {
            CurrentFileIndex = 0;
            TotalFiles = 0;
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
