using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Acquisition.ViewModels;
using Sufni.App.Bikes.ViewModels.ItemLists;
using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.LiveDaq.ViewModels.ItemLists;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.Sessions.Lists.ViewModels.ItemLists;
using Sufni.App.Setups.ViewModels.ItemLists;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.ViewModels;
using Sufni.App.SyncAndPairing.ViewModels.ItemLists;
using Sufni.App.Theming;
using Sufni.App.Extensibility.Capabilities;
namespace Sufni.App.Shell.ViewModels;

public partial class MainPagesViewModel : ViewModelBase
{
    private readonly IAppDataRefresher appDataRefresher;
    private readonly IImportSessionsCoordinator importSessionsCoordinator;
    private readonly ITrackCoordinator trackCoordinator;
    private readonly ISyncCoordinator syncCoordinator;
    private readonly IShellCoordinator shell;
    private readonly IThemeService themeService;
    private readonly IReadOnlyList<IExtensionStateRefreshParticipant> extensionStateRefreshParticipants;
    private MainPrimaryPageViewModel? activePrimaryPage;

    #region Observable properties

    [ObservableProperty] private bool databaseLoaded;
    [ObservableProperty] private int selectedPrimaryIndex;
    [ObservableProperty] private bool syncInProgress;
    [ObservableProperty] private string syncProgressText = string.Empty;
    [ObservableProperty] private double syncProgressValue;
    [ObservableProperty] private bool syncProgressIsIndeterminate = true;
    [ObservableProperty] private bool isPaired;
    [ObservableProperty] private bool isDrawerOpen;
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
    public ObservableCollection<MainPrimaryPageViewModel> PrimaryPages { get; } = [];
    public IReadOnlyList<AppToolbarCommandContribution> ExtensionToolbarCommands { get; }
    public IReadOnlyList<AppToolbarViewContribution> ExtensionToolbarViews { get; }
    public ViewModelBase SelectedPrimaryPageContent => GetSelectedPrimaryPage();

    #region Constructors

    public MainPagesViewModel(
        IAppDataRefresher appDataRefresher,
        IImportSessionsCoordinator importSessionsCoordinator,
        ITrackCoordinator trackCoordinator,
        ISyncCoordinator syncCoordinator,
        IShellCoordinator shell,
        IThemeService themeService,
        BikeListViewModel bikesPage,
        SessionListViewModel sessionsPage,
        SetupListViewModel setupsPage,
        LiveDaqListViewModel liveDaqsPage,
        ImportSessionsViewModel importSessionsPage,
        PairedDeviceListViewModel pairedDevicesPage,
        IUiThreadDispatcher uiThreadDispatcher,
        IEnumerable<IAppToolbarContributionProvider>? appToolbarContributionProviders = null,
        PairingClientViewModel? pairingClientPage = null,
        PairingServerViewModel? pairingServerViewModel = null,
        IEnumerable<IExtensionStateRefreshParticipant>? extensionStateRefreshParticipants = null)
        : base(uiThreadDispatcher)
    {
        this.appDataRefresher = appDataRefresher;
        this.importSessionsCoordinator = importSessionsCoordinator;
        this.trackCoordinator = trackCoordinator;
        this.syncCoordinator = syncCoordinator;
        this.shell = shell;
        this.themeService = themeService;
        this.extensionStateRefreshParticipants = extensionStateRefreshParticipants?.ToArray() ?? [];
        BikesPage = bikesPage;
        SessionsPage = sessionsPage;
        SetupsPage = setupsPage;
        LiveDaqsPage = liveDaqsPage;
        ImportSessionsPage = importSessionsPage;
        PairedDevicesPage = pairedDevicesPage;
        PairingClientPage = pairingClientPage;
        PairingServerViewModel = pairingServerViewModel;
        var toolbarContributions = BuildExtensionToolbarContributions(appToolbarContributionProviders);
        ExtensionToolbarCommands = toolbarContributions.Commands;
        ExtensionToolbarViews = toolbarContributions.Views;
        PrimaryPages.Add(new MainPrimaryPageViewModel(
            MainPrimaryPageRole.Sessions,
            "Sessions",
            "/Assets/fa-chart-line.svg",
            SessionsPage));
        PrimaryPages.Add(new MainPrimaryPageViewModel(
            MainPrimaryPageRole.Setups,
            "Setups",
            "/Assets/cog.svg",
            SetupsPage));
        PrimaryPages.Add(new MainPrimaryPageViewModel(
            MainPrimaryPageRole.Bikes,
            "Bikes",
            "/Assets/fa-person-mountainbiking.svg",
            BikesPage));
        PrimaryPages.Add(new MainPrimaryPageViewModel(
            MainPrimaryPageRole.LiveDaqs,
            "Live",
            "/Assets/fa-link.svg",
            LiveDaqsPage));
        activePrimaryPage = GetSelectedPrimaryPageDescriptor();

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

    private static AppToolbarContributionSet BuildExtensionToolbarContributions(
        IEnumerable<IAppToolbarContributionProvider>? providers)
    {
        if (providers is null)
        {
            return new AppToolbarContributionSet([], []);
        }

        var contributions = new List<IExtensionContribution>();
        var contributionIds = new ExtensionContributionValidator.ContributionIdTracker("app toolbar contributions");
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ExtensionContributionValidator.ValidateRequiredId(
                provider.ExtensionId,
                "App toolbar contribution provider");
            foreach (var contribution in provider.CreateCommandContributions())
            {
                ExtensionContributionValidator.ValidateAppToolbarContribution(
                    contribution,
                    provider.ExtensionId,
                    contributionIds);
                contributions.Add(contribution);
            }

            foreach (var contribution in provider.CreateViewContributions())
            {
                ExtensionContributionValidator.ValidateAppToolbarContribution(
                    contribution,
                    provider.ExtensionId,
                    contributionIds);
                contributions.Add(contribution);
            }
        }

        var orderedContributions = contributions
            .OrderBy(contribution => contribution.Order)
            .ThenBy(contribution => contribution.ExtensionId, StringComparer.Ordinal)
            .ThenBy(contribution => contribution.ContributionId, StringComparer.Ordinal)
            .ToArray();

        return new AppToolbarContributionSet(
            orderedContributions.OfType<AppToolbarCommandContribution>().ToArray(),
            orderedContributions.OfType<AppToolbarViewContribution>().ToArray());
    }

    private sealed record AppToolbarContributionSet(
        IReadOnlyList<AppToolbarCommandContribution> Commands,
        IReadOnlyList<AppToolbarViewContribution> Views);

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

        await appDataRefresher.RefreshAsync();
        foreach (var participant in extensionStateRefreshParticipants)
        {
            await participant.RefreshExtensionStateAsync();
        }

        DatabaseLoaded = true;
    }

    private ViewModelBase GetSelectedPrimaryPage()
    {
        return GetSelectedPrimaryPageDescriptor().Content;
    }

    private MainPrimaryPageViewModel GetSelectedPrimaryPageDescriptor()
    {
        if (SelectedPrimaryIndex >= 0 && SelectedPrimaryIndex < PrimaryPages.Count)
        {
            return PrimaryPages[SelectedPrimaryIndex];
        }

        return PrimaryPages[0];
    }

    partial void OnSelectedPrimaryIndexChanged(int value)
    {
        var nextPage = GetSelectedPrimaryPageDescriptor();
        if (ReferenceEquals(activePrimaryPage, nextPage)) return;

        if (activePrimaryPage?.Role == MainPrimaryPageRole.LiveDaqs)
        {
            LiveDaqsPage.Deactivate();
        }

        if (nextPage.Role == MainPrimaryPageRole.LiveDaqs)
        {
            LiveDaqsPage.Activate();
        }

        activePrimaryPage = nextPage;
        OnPropertyChanged(nameof(SelectedPrimaryPageContent));
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
    private void OpenDrawer()
    {
        IsDrawerOpen = true;
    }

    [RelayCommand]
    private void OpenClosePairedDevicesList()
    {
        IsPairedDevicesListOpen = !IsPairedDevicesListOpen;
    }

    [RelayCommand]
    private void OpenPage(ViewModelBase view)
    {
        IsDrawerOpen = false;
        shell.Open(view);
    }

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
