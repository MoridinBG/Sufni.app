using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Sufni.App.ViewModels;

public partial class ImportSessionsViewModel : TabPageViewModelBase
{
    #region Observable properties

    public ObservableCollection<ITelemetryDataStore>? TelemetryDataStores { get; set; }
    public ObservableCollection<ITelemetryFile> TelemetryFiles { get; } = [];

    [ObservableProperty] private ITelemetryDataStore? selectedDataStore;
    [ObservableProperty] private bool newDataStoresAvailable;
    [ObservableProperty] private bool importInProgress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportSessionsCommand))]
    private Guid? selectedSetup;

    #endregion Observable properties

    #region Property change handlers

    private async void GetDataStoreFiles(object? dataStore)
    {
        ImportInProgress = true;

        TelemetryFiles.Clear();
        var files = await (dataStore as ITelemetryDataStore)!.GetFiles();
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var file in files)
            {
                TelemetryFiles.Add(file);
            }
        });

        ImportInProgress = false;
    }

    async partial void OnSelectedDataStoreChanged(ITelemetryDataStore? value)
    {
        if (value == null)
        {
            TelemetryFiles.Clear();
            SelectedSetup = null;
            return;
        }

        // Need to clear the DataStoresAvailable flag so that the notification does not show
        // up when the first datastore appears and auto-selected.
        ClearNewDataStoresAvailable();

        try
        {
            var boards = await databaseService.GetAllAsync<Board>();
            var selectedBoard = boards.FirstOrDefault(b => b?.Id == value.BoardId, null);
            SelectedSetup = selectedBoard?.SetupId;
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Error while changing data store: {e.Message}");
        }

        new Thread(GetDataStoreFiles).Start(value);
    }

    #endregion Property change handlers

    #region Private members

    private readonly IDatabaseService databaseService;
    private readonly ITelemetryDataStoreService telemetryDataStoreService;
    private readonly IFilesService filesService;
    private readonly ISetupCoordinator setupCoordinator;
    private readonly IImportSessionsCoordinator importSessionsCoordinator;
    private readonly ISetupStore? setupStore;
    private CompositeDisposable? subscriptions;

    #endregion Private members

    #region Constructors

    public ImportSessionsViewModel()
    {
        Name = "Import Sessions";
        databaseService = null!;
        telemetryDataStoreService = null!;
        filesService = null!;
        setupCoordinator = null!;
        importSessionsCoordinator = null!;
        setupStore = null;
    }

    public ImportSessionsViewModel(
        IDatabaseService databaseService,
        ITelemetryDataStoreService telemetryDataStoreService,
        IFilesService filesService,
        IShellCoordinator shell,
        IDialogService dialogService,
        ISetupCoordinator setupCoordinator,
        IImportSessionsCoordinator importSessionsCoordinator,
        ISetupStore setupStore)
        : base(shell, dialogService)
    {
        Name = "Import Sessions";
        this.databaseService = databaseService;
        this.telemetryDataStoreService = telemetryDataStoreService;
        this.filesService = filesService;
        this.setupCoordinator = setupCoordinator;
        this.importSessionsCoordinator = importSessionsCoordinator;
        this.setupStore = setupStore;

        TelemetryDataStores = telemetryDataStoreService.DataStores;
        TelemetryDataStores.CollectionChanged += (_, e) =>
        {
            var comparer = new TelemetryDataStoreComparer();
            var removed = (ITelemetryDataStore)e.OldItems?[0]!;
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        NewDataStoresAvailable = true;
                        SelectedDataStore ??= TelemetryDataStores[0];
                    });
                    break;
                case NotifyCollectionChangedAction.Remove:
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        if (TelemetryDataStores.Count == 0 || !comparer.Equals(SelectedDataStore, removed)) return;
                        // XXX: The files from the correct datastore show up, but the ComboBox won't show the datastore
                        //      as selected. Probably has something to do with this fix, since it only handle adds:
                        //      https://github.com/AvaloniaUI/Avalonia/pull/4593/commits/8dfc65d17be00b7f7c96c294dabe7616916951b2
                        SelectedDataStore = TelemetryDataStores[^1];
                    });
                    break;
                case NotifyCollectionChangedAction.Replace:
                case NotifyCollectionChangedAction.Move:
                case NotifyCollectionChangedAction.Reset:
                    return;
            }
        };
        if (TelemetryDataStores.Count > 0)
        {
            SelectedDataStore = TelemetryDataStores[0];
        }
    }

    #endregion

    #region Public methods

    public async Task EvaluateSetupExists()
    {
        var boards = await databaseService.GetAllAsync<Board>();
        var selectedBoard = boards.FirstOrDefault(b => b?.Id == SelectedDataStore?.BoardId, null);
        SelectedSetup = selectedBoard?.SetupId;
    }

    #endregion

    #region Commands

    [RelayCommand]
    private async Task OpenDataStore()
    {
        Debug.Assert(TelemetryDataStores != null, nameof(TelemetryDataStores) + " != null");

        var folder = await filesService.OpenDataStoreFolderAsync();
        if (folder is null) return;

        var massStorages = TelemetryDataStores.OfType<MassStorageTelemetryDataStore>()
            .Select(ds => ds.DriveInfo.RootDirectory.FullName)
            .ToArray();
        var folderLocalPath = folder.TryGetLocalPath();
        if (massStorages.Contains(folderLocalPath))
        {
            Notifications.Add("Folder is already opened in mass-storage mode!");
            return;
        }

        var dataStore = new StorageProviderTelemetryDataStore(folder);
        await dataStore.Initialization;

        TelemetryDataStores.Add(dataStore);
        SelectedDataStore = dataStore;
    }

    private async void ImportSessionsInternal()
    {
        try
        {
            Debug.Assert(SelectedSetup != null);
            Debug.Assert(SelectedDataStore != null);

            ImportInProgress = true;

            // The import is launched on a non-pool Thread, so a
            // Progress<T>'s captured sync context will not be the UI
            // thread — marshal manually with Dispatcher.UIThread.Post
            // so per-file events stream into the UI as they happen.
            var progress = new Progress<SessionImportEvent>();
            progress.ProgressChanged += (_, evt) => Dispatcher.UIThread.Post(() =>
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

            // The coordinator owns the per-file lifecycle (psst generation,
            // PutSessionAsync, OnImported, store upsert, OnTrashed for
            // unimported files). The VM keeps the data-store browse and
            // file-list refresh because they are screen-scoped UI state.
            await importSessionsCoordinator.ImportAsync(
                SelectedDataStore,
                TelemetryFiles.ToList(),
                SelectedSetup!.Value,
                progress);

            var files = await SelectedDataStore.GetFiles();
            TelemetryFiles.Clear();
            foreach (var file in files)
            {
                TelemetryFiles.Add(file);
            }

            ImportInProgress = false;
        }
        catch (Exception e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ErrorMessages.Add($"Import failed: {e.Message}");
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanImportSessions))]
    private void ImportSessions()
    {
        new Thread(ImportSessionsInternal).Start();
    }

    private bool CanImportSessions()
    {
        return SelectedSetup != null;
    }

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
        telemetryDataStoreService.StartBrowse();

        // Re-resolve SelectedSetup from the store whenever a setup is
        // added/updated/removed (e.g. saved from the welcome flow). This
        // supersedes the legacy `EvaluateSetupExists` push from the
        // setup-saved code path. Tied to page lifecycle so each
        // show/hide builds a fresh subscription — the import VM is a
        // singleton so a constructor-time subscription would silently
        // drop on first close.
        if (setupStore is not null && subscriptions is null)
        {
            subscriptions = new CompositeDisposable();
            subscriptions.Add(setupStore.Connect().Subscribe(_ =>
                Dispatcher.UIThread.Post(() =>
                {
                    var boardId = SelectedDataStore?.BoardId;
                    if (boardId is null)
                    {
                        SelectedSetup = null;
                        return;
                    }

                    SelectedSetup = setupStore.FindByBoardId(boardId.Value)?.Id;
                })));
        }
    }

    [RelayCommand]
    private void Unloaded()
    {
        TelemetryFiles.Clear();
        telemetryDataStoreService.StopBrowse();

        subscriptions?.Dispose();
        subscriptions = null;
    }

    #endregion Commands
}