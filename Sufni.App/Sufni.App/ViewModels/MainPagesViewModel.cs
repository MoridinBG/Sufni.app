using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Factories;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels;

public partial class MainPagesViewModel : ViewModelBase
{
    private readonly IDatabaseService databaseService;
    private readonly IBikeStoreWriter bikeStoreWriter;
    private readonly ISetupStoreWriter setupStoreWriter;
    private readonly ISessionViewModelFactory sessionViewModelFactory;
    private readonly IFilesService filesService;
    private readonly ISynchronizationClientService? synchronizationClientService;
    private readonly INavigator navigator;
    private readonly IDialogService dialogService;
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
        sessionViewModelFactory = null!;
        filesService = null!;
        synchronizationClientService = null;
        navigator = null!;
        dialogService = null!;
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
        ISessionViewModelFactory sessionViewModelFactory,
        IFilesService filesService,
        INavigator navigator,
        IDialogService dialogService,
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
        this.sessionViewModelFactory = sessionViewModelFactory;
        this.filesService = filesService;
        this.navigator = navigator;
        this.dialogService = dialogService;
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
        SessionsPage.MenuItems.Add(new("import", OpenPageCommand, importSessionsPage));

        if (synchronizationServer is not null)
        {
            // update UI when entities arrive from synced device
            synchronizationServer.SynchronizationDataArrived = data =>
            {
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await MergeFromDatabase(data);
                });
            };

            // update UI when session data arrives from synced device
            synchronizationServer.SessionDataArrived = id =>
            {
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var data = await databaseService.GetSessionPsstAsync(id);

                    if (SessionsPage.Source.Items.First(s => s.Id == id) is SessionViewModel session)
                    {
                        session.TelemetryData = data;
                        SessionsPage.Source.AddOrUpdate(session);
                        SessionsPage.Source.Refresh();
                    }
                });
            };

            synchronizationServer.PairingConfirmed += (_, e) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (e is not PairingEventArgs pdea) return;
                    PairedDevicesPage.Source.AddOrUpdate(new PairedDeviceViewModel(pdea.Device, navigator, dialogService, PairedDevicesPage));
                });
            };
            synchronizationServer.Unpaired += (_, e) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (e is not PairingEventArgs pdea) return;
                    PairedDevicesPage.Source.Remove(new PairedDeviceViewModel(pdea.Device, navigator, dialogService, PairedDevicesPage));
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
        await SessionsPage.LoadFromDatabase();
        await PairedDevicesPage.LoadFromDatabase();

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

        foreach (var session in data.Sessions)
        {
            if (session.Deleted is not null)
            {
                SessionsPage.Source.RemoveKey(session.Id);
            }
            else
            {
                SessionsPage.Source.AddOrUpdate(sessionViewModelFactory.Create(session, true, SessionsPage));
            }
            SessionsPage.Source.Refresh();
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