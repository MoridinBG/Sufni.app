using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Theming;
using Sufni.App.ViewModels.ItemLists;

namespace Sufni.App.ViewModels;

public partial class MainPagesViewModel : ViewModelBase
{
    private readonly IBikeStore bikeStore;
    private readonly ISetupStore setupStore;
    private readonly ISessionStore sessionStore;
    private readonly IRecordedSessionSourceStore recordedSessionSourceStore;
    private readonly IPairedDeviceStore pairedDeviceStore;
    private readonly ImportSessionsCoordinator importSessionsCoordinator;
    private readonly TrackCoordinator trackCoordinator;
    private readonly SyncCoordinator syncCoordinator;
    private readonly IShellCoordinator shell;
    private readonly IThemeService themeService;
    private readonly ItemListViewModelBase[] primaryPages;
    private ItemListViewModelBase? activePrimaryPage;

    #region Observable properties

    [ObservableProperty] private bool databaseLoaded;
    [ObservableProperty] private int selectedIndex;
    [ObservableProperty] private bool syncInProgress;
    [ObservableProperty] private string syncProgressText = string.Empty;
    [ObservableProperty] private double syncProgressValue;
    [ObservableProperty] private bool syncProgressIsIndeterminate = true;
    [ObservableProperty] private bool isPaired;
    [ObservableProperty] private bool isMenuPaneOpen;
    [ObservableProperty] private bool isPairedDevicesListOpen;
    [ObservableProperty] private SufniThemeMode currentThemeMode;
    [ObservableProperty] private SufniThemeMode effectiveThemeMode;
    [ObservableProperty] private SufniThemeMode nextThemeMode;
    [ObservableProperty] private bool isSystemThemeAvailable;

    #endregion

    public ImportSessionsViewModel ImportSessionsPage { get; init; }
    public BikeListViewModel BikesPage { get; init; }
    public SetupListViewModel SetupsPage { get; init; }
    public SessionListViewModel SessionsPage { get; init; }
    public LiveDaqListViewModel LiveDaqsPage { get; init; }
    public PairedDeviceListViewModel PairedDevicesPage { get; init; }
    public PairingClientViewModel? PairingClientPage { get; init; }
    public PairingServerViewModel? PairingServerViewModel { get; init; }

    #region Constructors

    public MainPagesViewModel(
        IBikeStore bikeStore,
        ISetupStore setupStore,
        ISessionStore sessionStore,
        IRecordedSessionSourceStore recordedSessionSourceStore,
        IPairedDeviceStore pairedDeviceStore,
        ImportSessionsCoordinator importSessionsCoordinator,
        TrackCoordinator trackCoordinator,
        SyncCoordinator syncCoordinator,
        IShellCoordinator shell,
        IThemeService themeService,
        BikeListViewModel bikesPage,
        SessionListViewModel sessionsPage,
        SetupListViewModel setupsPage,
        LiveDaqListViewModel liveDaqsPage,
        ImportSessionsViewModel importSessionsPage,
        PairedDeviceListViewModel pairedDevicesPage,
        PairingClientViewModel? pairingClientPage = null,
        PairingServerViewModel? pairingServerViewModel = null)
    {
        this.bikeStore = bikeStore;
        this.setupStore = setupStore;
        this.sessionStore = sessionStore;
        this.recordedSessionSourceStore = recordedSessionSourceStore;
        this.pairedDeviceStore = pairedDeviceStore;
        this.importSessionsCoordinator = importSessionsCoordinator;
        this.trackCoordinator = trackCoordinator;
        this.syncCoordinator = syncCoordinator;
        this.shell = shell;
        this.themeService = themeService;
        BikesPage = bikesPage;
        SessionsPage = sessionsPage;
        SetupsPage = setupsPage;
        LiveDaqsPage = liveDaqsPage;
        ImportSessionsPage = importSessionsPage;
        PairedDevicesPage = pairedDevicesPage;
        PairingClientPage = pairingClientPage;
        PairingServerViewModel = pairingServerViewModel;
        primaryPages = [SessionsPage, SetupsPage, BikesPage, LiveDaqsPage];
        activePrimaryPage = GetSelectedPrimaryPage();

        BikesPage.MenuItems.Add(new("sync", SyncCommand));
        BikesPage.MenuItems.Add(new("add", BikesPage.AddCommand));
        SetupsPage.MenuItems.Add(new("sync", SyncCommand));
        SetupsPage.MenuItems.Add(new("add", SetupsPage.AddCommand));
        SessionsPage.MenuItems.Add(new("sync", SyncCommand));
        SessionsPage.MenuItems.Add(new("import", OpenImportCommand));

        syncCoordinator.SyncCompleted += OnSyncCompleted;
        syncCoordinator.SyncFailed += OnSyncFailed;
        syncCoordinator.IsRunningChanged += OnSyncIsRunningChanged;
        syncCoordinator.IsPairedChanged += OnSyncIsPairedChanged;
        syncCoordinator.CanSyncChanged += OnSyncCanSyncChanged;
        syncCoordinator.ProgressChanged += OnSyncProgressChanged;

        themeService.ThemeChanged += OnThemeChanged;

        // Seed the mirrors from the coordinator's current state in case
        // any of them already changed before construction (e.g. the
        // pairing-client coordinator's startup IsPairedAsync probe).
        SyncInProgress = syncCoordinator.IsRunning;
        IsPaired = syncCoordinator.IsPaired;
        SyncProgressState();
        SyncThemeState();

        _ = LoadDatabaseContent();
    }

    private void OnSyncCompleted(object? sender, SyncCompletedEventArgs e)
    {
        var currentPage = GetSelectedPrimaryPage();
        currentPage.Notifications.Add(e.Message);
        currentPage.ErrorMessages.Clear();
    }

    private void OnSyncFailed(object? sender, SyncFailedEventArgs e)
    {
        GetSelectedPrimaryPage().ErrorMessages.Add(e.ErrorMessage);
    }

    private void OnSyncIsRunningChanged(object? sender, EventArgs e)
    {
        SyncInProgress = syncCoordinator.IsRunning;
        SyncProgressState();
    }

    private void OnSyncIsPairedChanged(object? sender, EventArgs e)
    {
        IsPaired = syncCoordinator.IsPaired;
    }

    private void OnSyncCanSyncChanged(object? sender, EventArgs e)
    {
        SyncCommand.NotifyCanExecuteChanged();
    }

    private void OnSyncProgressChanged(object? sender, EventArgs e)
    {
        SyncProgressState();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        SyncThemeState();
    }

    #endregion Constructors

    #region Private methods

    private async Task LoadDatabaseContent()
    {
        DatabaseLoaded = false;

        await bikeStore.RefreshAsync();
        await setupStore.RefreshAsync();
        await sessionStore.RefreshAsync();
        await recordedSessionSourceStore.RefreshAsync();
        await pairedDeviceStore.RefreshAsync();

        DatabaseLoaded = true;
    }

    private ItemListViewModelBase GetSelectedPrimaryPage()
    {
        if (SelectedIndex >= 0 && SelectedIndex < primaryPages.Length)
        {
            return primaryPages[SelectedIndex];
        }

        return primaryPages[0];
    }

    partial void OnSelectedIndexChanged(int value)
    {
        var nextPage = GetSelectedPrimaryPage();
        if (ReferenceEquals(activePrimaryPage, nextPage)) return;

        if (activePrimaryPage is LiveDaqListViewModel previousLivePage)
        {
            previousLivePage.Deactivate();
        }

        if (nextPage is LiveDaqListViewModel nextLivePage)
        {
            nextLivePage.Activate();
        }

        activePrimaryPage = nextPage;
    }

    private void SyncProgressState()
    {
        var currentProgress = syncCoordinator.Progress;
        SyncProgressText = currentProgress?.Message ?? (SyncInProgress ? "Syncing" : string.Empty);
        SyncProgressValue = currentProgress?.Fraction ?? 0;
        SyncProgressIsIndeterminate = currentProgress is null || !currentProgress.IsDeterminate;
    }

    #endregion

    #region Commands

    private bool CanSync()
    {
        return syncCoordinator.CanSync;
    }

    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task Sync()
    {
        await syncCoordinator.SyncAllAsync();
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
    private void OpenPage(ViewModelBase view) => shell.Open(view);

    [RelayCommand]
    private async Task OpenImport() => await importSessionsCoordinator.OpenAsync();

    [RelayCommand]
    private async Task OpenGpsTracks()
    {
        var result = await trackCoordinator.ImportGpxAsync();
        PublishGpxImportResult(result);
    }

    [RelayCommand]
    private async Task ToggleTheme()
    {
        await themeService.ToggleAsync();
    }

    #endregion

    private void SyncThemeState()
    {
        CurrentThemeMode = themeService.Mode;
        EffectiveThemeMode = themeService.EffectiveMode;
        IsSystemThemeAvailable = themeService.IsSystemThemeAvailable;
        NextThemeMode = ResolveNextThemeMode(CurrentThemeMode, IsSystemThemeAvailable);
    }

    private static SufniThemeMode ResolveNextThemeMode(SufniThemeMode current, bool systemThemeAvailable)
        => current switch
        {
            SufniThemeMode.Dark => SufniThemeMode.Light,
            SufniThemeMode.Light when systemThemeAvailable => SufniThemeMode.System,
            SufniThemeMode.Light => SufniThemeMode.Dark,
            _ => SufniThemeMode.Dark
        };

    private void PublishGpxImportResult(GpxImportResult result)
    {
        if (result.ImportedCount == 0 && result.AlreadyImportedCount == 0)
        {
            return;
        }

        var currentPage = GetSelectedPrimaryPage();
        if (result.ImportedCount > 0 && result.AlreadyImportedCount > 0)
        {
            currentPage.Notifications.Add(
                $"Imported {result.ImportedCount} GPX track(s); skipped {result.AlreadyImportedCount} already imported track(s).");
            return;
        }

        if (result.ImportedCount > 0)
        {
            currentPage.Notifications.Add($"Imported {result.ImportedCount} GPX track(s).");
            return;
        }

        currentPage.Notifications.Add(
            result.AlreadyImportedCount == 1
                ? "GPX track is already imported."
                : $"{result.AlreadyImportedCount} GPX tracks are already imported.");
    }
}
