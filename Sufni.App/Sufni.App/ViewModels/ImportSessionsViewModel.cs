using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Sufni.App.ViewModels.Factories;
using Sufni.App.ViewModels.Items;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels;

public partial class ImportSessionsViewModel : TabPageViewModelBase, IImportSessionsOpener
{
    #region IImportSessionsOpener

    public void OpenImportSessions() => navigator.OpenPage(this);

    #endregion IImportSessionsOpener

    #region Observable properties

    public ObservableCollection<ITelemetryDataStore>? TelemetryDataStores { get; set; }
    public ObservableCollection<ITelemetryFile> TelemetryFiles { get; } = [];
    private readonly ISessionSink sessionSink;

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
    private readonly ISessionViewModelFactory sessionViewModelFactory;
    private readonly IFilesService filesService;
    private readonly ISetupCoordinator setupCoordinator;

    #endregion Private members

    #region Constructors

    public ImportSessionsViewModel()
    {
        Name = "Import Sessions";
        databaseService = null!;
        telemetryDataStoreService = null!;
        sessionViewModelFactory = null!;
        filesService = null!;
        sessionSink = null!;
        setupCoordinator = null!;
    }

    public ImportSessionsViewModel(
        IDatabaseService databaseService,
        ITelemetryDataStoreService telemetryDataStoreService,
        ISessionViewModelFactory sessionViewModelFactory,
        IFilesService filesService,
        ISessionSink sessionSink,
        INavigator navigator,
        IDialogService dialogService,
        ISetupCoordinator setupCoordinator)
        : base(navigator, dialogService)
    {
        Name = "Import Sessions";
        this.databaseService = databaseService;
        this.telemetryDataStoreService = telemetryDataStoreService;
        this.sessionViewModelFactory = sessionViewModelFactory;
        this.filesService = filesService;
        this.sessionSink = sessionSink;
        this.setupCoordinator = setupCoordinator;

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

            var setup = await databaseService.GetAsync<Setup>(SelectedSetup!.Value) ?? throw new Exception("Setup is missing");
            var bike = await databaseService.GetAsync<Bike>(setup.BikeId);
            Debug.Assert(bike is not null);

            var frontSensorConfiguration = setup.FrontSensorConfiguration(bike);
            var rearSensorConfiguration = setup.RearSensorConfiguration(bike);

            var bikeData = new BikeData(
                bike.HeadAngle,
                frontSensorConfiguration?.MaxTravel,
                rearSensorConfiguration?.MaxTravel,
                frontSensorConfiguration?.MeasurementToTravel,
                rearSensorConfiguration?.MeasurementToTravel);

            foreach (var telemetryFile in TelemetryFiles.Where(f => f.ShouldBeImported.HasValue && f.ShouldBeImported.Value))
            {
                try
                {
                    var psst = await telemetryFile.GeneratePsstAsync(bikeData);

                    var session = new Session(
                        id: Guid.NewGuid(),
                        name: telemetryFile.Name,
                        description: telemetryFile.Description,
                        setup: SelectedSetup!.Value,
                        timestamp: (int)((DateTimeOffset)telemetryFile.StartTime).ToUnixTimeSeconds())
                    {
                        ProcessedData = psst
                    };

                    await databaseService.PutSessionAsync(session);
                    await telemetryFile.OnImported();

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var svm = sessionViewModelFactory.Create(session, true, sessionSink);
                        sessionSink.Add(svm);
                        Notifications.Insert(0, $"{svm.Name} was successfully imported.");
                    });
                }
                catch (Exception e)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        ErrorMessages.Add($"Could not import {telemetryFile.Name}: {e.Message}");
                    });
                }
            }

            foreach (var telemetryFile in TelemetryFiles.Where(f => !f.ShouldBeImported.HasValue))
            {
                await telemetryFile.OnTrashed();
            }

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
    }

    [RelayCommand]
    private void Unloaded()
    {
        TelemetryFiles.Clear();
        telemetryDataStoreService.StopBrowse();
    }

    #endregion Commands
}