using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Services;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels;

public partial class MainPagesViewModel : ViewModelBase
{
    #region Observable properties

    [ObservableProperty] private bool databaseLoaded;
    [ObservableProperty] private ImportSessionsViewModel importSessionsPage;
    [ObservableProperty] private SetupListViewModel setupsPage;
    [ObservableProperty] private SessionListViewModel sessionsPage;
    [ObservableProperty] private SettingsViewModel settingsPage = new();
    [ObservableProperty] private int selectedIndex;
    [ObservableProperty] private bool syncInProgress;
    [ObservableProperty] private bool isMenuPaneOpen;

    #endregion

    private readonly IDatabaseService? databaseService;
    private readonly ItemListViewModelBase[] pages;

    public MainPagesViewModel()
    {
        databaseService = App.Current?.Services?.GetService<IDatabaseService>();
        SessionsPage = new SessionListViewModel();
        ImportSessionsPage = new ImportSessionsViewModel(SessionsPage.Source);
        SetupsPage = new SetupListViewModel(ImportSessionsPage);
        pages = [SessionsPage, SetupsPage];

        SetupsPage.MenuItems.Add(new("sync", SyncCommand));
        SetupsPage.MenuItems.Add(new("add", SetupsPage.AddCommand));
        SessionsPage.MenuItems.Add(new("sync", SyncCommand));
        SessionsPage.MenuItems.Add(new("import", OpenPageCommand, importSessionsPage));

        SettingsPage.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(SettingsPage.IsRegistered))
            {
                return;
            }

            SyncCommand.NotifyCanExecuteChanged();
        };

        _ = LoadDatabaseContent();
    }

    #region Public methods

    public async Task DeleteItem(ItemViewModelBase item)
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        switch (item)
        {
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
            case SetupViewModel svm:
                SetupsPage.UndoableDelete(svm);
                break;
            case SessionViewModel svm:
                SessionsPage.UndoableDelete(svm);
                break;
        }
    }

    #endregion

    #region Private methods

    private async Task LoadDatabaseContent()
    {
        DatabaseLoaded = false;

        await SetupsPage.LoadFromDatabase();
        await SessionsPage.LoadFromDatabase();

        DatabaseLoaded = true;
    }

    #endregion

    #region Commands

    private bool CanSync()
    {
        return SettingsPage.IsRegistered;
    }

    private async void SyncInternal()
    {
        var synchronizationService = App.Current?.Services?.GetService<ISynchronizationService>();
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
    private void ShowConnectPage()
    {
        var mainViewModel = App.Current?.Services?.GetService<MainViewModel>();
        Debug.Assert(mainViewModel != null, nameof(mainViewModel) + " != null");
        mainViewModel.OpenView(SettingsPage);
    }

    [RelayCommand]
    private void ShowImportPage()
    {
        var mainViewModel = App.Current?.Services?.GetService<MainViewModel>();
        Debug.Assert(mainViewModel != null, nameof(mainViewModel) + " != null");
        mainViewModel.OpenView(ImportSessionsPage);
    }

    [RelayCommand]
    private void OpenMenuPane()
    {
        IsMenuPaneOpen = true;
    }

    #endregion
}