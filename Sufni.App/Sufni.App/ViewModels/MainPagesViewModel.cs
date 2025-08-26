using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels;

public partial class MainPagesViewModel : ViewModelBase
{
    private readonly IDatabaseService? databaseService;
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
        databaseService = App.Current?.Services?.GetService<IDatabaseService>();
        BikesPage = new BikeListViewModel();
        SessionsPage = new SessionListViewModel();
        PairedDevicesPage = new  PairedDeviceListViewModel();
        ImportSessionsPage = new ImportSessionsViewModel(SessionsPage.Source);
        SetupsPage = new SetupListViewModel(ImportSessionsPage, BikesPage);
        pages = [SessionsPage, SetupsPage];

        BikesPage.MenuItems.Add(new("sync", SyncCommand));
        BikesPage.MenuItems.Add(new("add", BikesPage.AddCommand));
        SetupsPage.MenuItems.Add(new("sync", SyncCommand));
        SetupsPage.MenuItems.Add(new("add", SetupsPage.AddCommand));
        SessionsPage.MenuItems.Add(new("sync", SyncCommand));
        SessionsPage.MenuItems.Add(new("import", OpenPageCommand, importSessionsPage));

        if (App.Current!.IsDesktop)
        {
            Debug.Assert(databaseService is  not null);
            Debug.Assert(App.Current is not null);

            var synchronizationServer = App.Current.Services?.GetService<ISynchronizationServerService>();
            Debug.Assert(synchronizationServer is not null);

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

            PairingServerViewModel = new();
        }
        else
        {
            PairingClientPage = new();
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

    #region Public methods

    public async Task DeleteItem(ItemViewModelBase item)
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        switch (item)
        {
            case BikeViewModel bvm:
                await BikesPage.Delete(bvm);
                break;
            case SetupViewModel svm:
                await SetupsPage.Delete(svm);
                break;
            case SessionViewModel svm:
                await SessionsPage.Delete(svm);
                break;
        }
    }

    public void UndoableDelete(ItemViewModelBase item)
    {
        switch (item)
        {
            case BikeViewModel bvm:
                BikesPage.UndoableDelete(bvm);
                break;
            case SetupViewModel svm:
                SetupsPage.UndoableDelete(svm);
                break;
            case SessionViewModel svm:
                SessionsPage.UndoableDelete(svm);
                break;
            case PairedDeviceViewModel pdvm:
                PairedDevicesPage.UndoableDelete(pdvm);
                break;
        }
    }

    #endregion

    #region Private methods

    private async Task LoadDatabaseContent()
    {
        DatabaseLoaded = false;

        await BikesPage.LoadFromDatabase();
        await SetupsPage.LoadFromDatabase();
        await SessionsPage.LoadFromDatabase();
        await PairedDevicesPage.LoadFromDatabase();

        DatabaseLoaded = true;
    }

    private async Task MergeFromDatabase(SynchronizationData data)
    {
        Debug.Assert(databaseService is not null);

        foreach (var bike in data.Bikes)
        {
            if (bike.Deleted is not null)
            {
                BikesPage.Source.RemoveKey(bike.Id);
            }
            else
            {
                BikesPage.Source.AddOrUpdate(new BikeViewModel(bike, true));
            }
            BikesPage.Source.Refresh();
        }

        var boards = await databaseService.GetAllAsync<Board>();
        foreach (var setup in data.Setups)
        {
            if (setup.Deleted is not null)
            {
                SetupsPage.Source.RemoveKey(setup.Id);
            }
            else
            {
                var board = boards.FirstOrDefault(b => b?.SetupId == setup.Id, null);
                SetupsPage.Source.AddOrUpdate(new SetupViewModel(setup, board?.Id, true, BikesPage.Source));
            }
            SetupsPage.Source.Refresh();
        }

        foreach (var session in data.Sessions)
        {
            if (session.Deleted is not null)
            {
                SessionsPage.Source.RemoveKey(session.Id);
            }
            else
            {
                SessionsPage.Source.AddOrUpdate(new SessionViewModel(session, true));
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
        var synchronizationService = App.Current?.Services?.GetService<ISynchronizationClientService>();
        Debug.Assert(synchronizationService != null, nameof(synchronizationService) + " != null");

        SyncInProgress = true;

        try
        {
            await synchronizationService.SyncAll();
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
    private async Task OpenGpsTracks()
    {
        var fileService = App.Current?.Services?.GetService<IFilesService>();
        Debug.Assert(fileService is not null);
        Debug.Assert(databaseService is not null);

        var files = await fileService.OpenGpxFilesAsync();
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