using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels.ItemLists;

namespace Sufni.App.ViewModels;

public partial class MainPagesViewModel : ViewModelBase
{
    private readonly IDatabaseService databaseService;
    private readonly IBikeStoreWriter bikeStoreWriter;
    private readonly ISetupStoreWriter setupStoreWriter;
    private readonly ISessionStoreWriter sessionStoreWriter;
    private readonly IPairedDeviceStoreWriter pairedDeviceStoreWriter;
    private readonly IImportSessionsCoordinator importSessionsCoordinator;
    private readonly IFilesService filesService;
    private readonly ISynchronizationClientService? synchronizationClientService;
    private readonly INavigator navigator;
    private readonly ItemListViewModelBase[] pages;

    #region Observable properties

    [ObservableProperty] private bool databaseLoaded;
    [ObservableProperty] private ImportSessionsViewModel importSessionsPage;
    [ObservableProperty] private BikeListViewModel bikesPage;
    [ObservableProperty] private SetupListViewModel setupsPage;
    [ObservableProperty] private SessionListViewModel sessionsPage;
    [ObservableProperty] private PairedDeviceListViewModel pairedDevicesPage;
    [ObservableProperty] private PairingClientViewModel? pairingClientPage;
    [ObservableProperty] private PairingServerViewModel? pairingServerViewModel;
    [ObservableProperty] private int selectedIndex;
    [ObservableProperty] private bool syncInProgress;
    [ObservableProperty] private bool isMenuPaneOpen;
    [ObservableProperty] private bool isPairedDevicesListOpen;

    #endregion

    #region Constructors

    public MainPagesViewModel()
    {
        databaseService = null!;
        bikeStoreWriter = null!;
        setupStoreWriter = null!;
        sessionStoreWriter = null!;
        pairedDeviceStoreWriter = null!;
        importSessionsCoordinator = null!;
        filesService = null!;
        synchronizationClientService = null;
        navigator = null!;
        importSessionsPage = new();
        bikesPage = new();
        setupsPage = new();
        sessionsPage = new();
        pairedDevicesPage = new();
        pages = [];
    }

    public MainPagesViewModel(
        IDatabaseService databaseService,
        IBikeStoreWriter bikeStoreWriter,
        ISetupStoreWriter setupStoreWriter,
        ISessionStoreWriter sessionStoreWriter,
        IPairedDeviceStoreWriter pairedDeviceStoreWriter,
        IImportSessionsCoordinator importSessionsCoordinator,
        IFilesService filesService,
        INavigator navigator,
        BikeListViewModel bikesPage,
        SessionListViewModel sessionsPage,
        SetupListViewModel setupsPage,
        ImportSessionsViewModel importSessionsPage,
        PairedDeviceListViewModel pairedDevicesPage,
        ISynchronizationServerService? synchronizationServer = null,
        ISynchronizationClientService? synchronizationClientService = null,
        PairingClientViewModel? pairingClientPage = null,
        PairingServerViewModel? pairingServerViewModel = null)
    {
        this.databaseService = databaseService;
        this.bikeStoreWriter = bikeStoreWriter;
        this.setupStoreWriter = setupStoreWriter;
        this.sessionStoreWriter = sessionStoreWriter;
        this.pairedDeviceStoreWriter = pairedDeviceStoreWriter;
        this.importSessionsCoordinator = importSessionsCoordinator;
        this.filesService = filesService;
        this.navigator = navigator;
        this.synchronizationClientService = synchronizationClientService;
        BikesPage = bikesPage;
        SessionsPage = sessionsPage;
        SetupsPage = setupsPage;
        ImportSessionsPage = importSessionsPage;
        PairedDevicesPage = pairedDevicesPage;
        PairingClientPage = pairingClientPage;
        PairingServerViewModel = pairingServerViewModel;
        pages = [SessionsPage, SetupsPage, BikesPage];

        BikesPage.MenuItems.Add(new("sync", SyncCommand));
        BikesPage.MenuItems.Add(new("add", BikesPage.AddCommand));
        SetupsPage.MenuItems.Add(new("sync", SyncCommand));
        SetupsPage.MenuItems.Add(new("add", SetupsPage.AddCommand));
        SessionsPage.MenuItems.Add(new("sync", SyncCommand));
        SessionsPage.MenuItems.Add(new("import", OpenImportCommand));

        if (synchronizationServer is not null)
        {
            // update bike/setup stores when entities arrive from synced
            // device. Sessions are owned by SessionCoordinator, paired
            // devices by PairedDeviceCoordinator — both subscribe to the
            // same events in their constructors (via +=).
            synchronizationServer.SynchronizationDataArrived += data =>
            {
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await MergeFromDatabase(data);
                });
            };
        }

        if (PairingClientPage is not null)
        {
            PairingClientPage.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(PairingClientPage.IsPaired))
                {
                    return;
                }

                SyncCommand.NotifyCanExecuteChanged();
            };
        }

        _ = LoadDatabaseContent();
    }

    #endregion Constructors

    #region Private methods

    private async Task LoadDatabaseContent()
    {
        DatabaseLoaded = false;

        await bikeStoreWriter.RefreshAsync();
        await setupStoreWriter.RefreshAsync();
        await sessionStoreWriter.RefreshAsync();
        await pairedDeviceStoreWriter.RefreshAsync();

        DatabaseLoaded = true;
    }

    private async Task MergeFromDatabase(SynchronizationData data)
    {
        foreach (var bike in data.Bikes)
        {
            if (bike.Deleted is not null)
            {
                bikeStoreWriter.Remove(bike.Id);
            }
            else
            {
                bikeStoreWriter.Upsert(BikeSnapshot.From(bike));
            }
        }

        var boards = await databaseService.GetAllAsync<Board>();
        foreach (var setup in data.Setups)
        {
            if (setup.Deleted is not null)
            {
                setupStoreWriter.Remove(setup.Id);
            }
            else
            {
                var board = boards.FirstOrDefault(b => b?.SetupId == setup.Id, null);
                setupStoreWriter.Upsert(SetupSnapshot.From(setup, board?.Id));
            }
        }
    }

    #endregion

    #region Commands

    private bool CanSync()
    {
        return PairingClientPage is { IsPaired: true };
    }

    private async void SyncInternal()
    {
        if (synchronizationClientService is null) return;

        SyncInProgress = true;

        try
        {
            await synchronizationClientService.SyncAll();
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await LoadDatabaseContent();

                pages[SelectedIndex].Notifications.Add("Sync successful");
                pages[SelectedIndex].ErrorMessages.Clear();
            });
        }
        catch (Exception e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                pages[SelectedIndex].ErrorMessages.Add($"Sync failed: {e.Message}");
            });
        }

        SyncInProgress = false;
    }

    [RelayCommand(CanExecute = nameof(CanSync))]
    private void Sync()
    {
        new Thread(SyncInternal).Start();
    }

    [RelayCommand]
    private void OpenMenuPane()
    {
        IsMenuPaneOpen = true;
    }

    [RelayCommand]
    private void OpenClosePairedDevicesList()
    {
        IsPairedDevicesListOpen = !IsPairedDevicesListOpen;
    }

    [RelayCommand]
    private void OpenPage(ViewModelBase view) => navigator.OpenPage(view);

    [RelayCommand]
    private async Task OpenImport() => await importSessionsCoordinator.OpenAsync();

    [RelayCommand]
    private async Task OpenGpsTracks()
    {
        var files = await filesService.OpenGpxFilesAsync();
        foreach (var file in files)
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var gpx = await reader.ReadToEndAsync();
            var track = Track.FromGpx(gpx);
            await databaseService.PutAsync(track);
        }
    }

    #endregion
}