using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Acquisition.ViewModels;
using Sufni.App.Bikes.ViewModels.ItemLists;
using Sufni.App.Infrastructure;
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
using Sufni.App.Video.Coordinators;
namespace Sufni.App.Shell.ViewModels;

public partial class MainPagesViewModel : ViewModelBase
{
    private readonly ITrackCoordinator trackCoordinator;
    private readonly IVideoCoordinator videoCoordinator;
    private readonly IShellCoordinator shell;
    private readonly IThemeService themeService;
    private readonly IAppEnvironment appEnvironment;
    private MainPrimaryPageViewModel? activePrimaryPage;

    #region Observable properties

    [ObservableProperty] public partial int SelectedPrimaryIndex { get; set; }
    [ObservableProperty] public partial bool IsDrawerOpen { get; set; }
    [ObservableProperty] public partial bool IsPairedDevicesListOpen { get; set; }
    [ObservableProperty] public partial SufniThemeMode CurrentThemeMode { get; set; }
    [ObservableProperty] public partial SufniThemeMode EffectiveThemeMode { get; set; }
    [ObservableProperty] public partial SufniThemeMode NextThemeMode { get; set; }
    [ObservableProperty] public partial bool IsSystemThemeAvailable { get; set; }
    [ObservableProperty] public partial UiLayoutProfile SelectedLayoutProfile { get; set; }

    #endregion

    public ShellFeedbackViewModel Feedback { get; }
    public BikeListViewModel BikesPage { get; init; }
    public SetupListViewModel SetupsPage { get; init; }
    public SessionListViewModel SessionsPage { get; init; }
    public LiveDaqListViewModel LiveDaqsPage { get; init; }
    public PairedDeviceListViewModel PairedDevicesPage { get; init; }
    public PairingClientViewModel? PairingClientPage { get; init; }
    public PairingServerViewModel? PairingServerViewModel { get; init; }
    public ObservableCollection<MainPrimaryPageViewModel> PrimaryPages { get; } = [];
    public ViewModelBase SelectedPrimaryPageContent => GetSelectedPrimaryPage();
    public bool CanImportSessions => Feedback.CanImportSessions;
    public bool CanImportGpsTracks => appEnvironment.Capabilities.SupportsStorageProviderImport;
    public bool CanOpenVideos => appEnvironment.Capabilities.CanPlayVideo;
    public bool CanHostSyncServer => appEnvironment.Capabilities.CanHostSyncServer;
    public bool CanShowPairingClientActions =>
        appEnvironment.Capabilities.CanPairAsClient && PairingClientPage is not null;
    public bool IsPairingRequestVisible =>
        CanHostSyncServer && !string.IsNullOrWhiteSpace(PairingServerViewModel?.PairingPin);
    public string LayoutProfileMenuHeader => $"layout: {FormatLayoutProfile(SelectedLayoutProfile)}";
    public string CompactLayoutProfileMenuText => FormatLayoutProfileMenuText(UiLayoutProfile.Compact);
    public string WorkspaceLayoutProfileMenuText => FormatLayoutProfileMenuText(UiLayoutProfile.Workspace);
    public UiLayoutProfile TargetLayoutProfile => SelectedLayoutProfile == UiLayoutProfile.Compact
        ? UiLayoutProfile.Workspace
        : UiLayoutProfile.Compact;
    public string LayoutProfileActionMenuText => FormatLayoutProfile(TargetLayoutProfile).ToLowerInvariant();

    #region Constructors

    public MainPagesViewModel(
        ITrackCoordinator trackCoordinator,
        ShellFeedbackViewModel feedback,
        IShellCoordinator shell,
        IThemeService themeService,
        IAppEnvironment appEnvironment,
        BikeListViewModel bikesPage,
        SessionListViewModel sessionsPage,
        SetupListViewModel setupsPage,
        LiveDaqListViewModel liveDaqsPage,
        PairedDeviceListViewModel pairedDevicesPage,
        IUiThreadDispatcher uiThreadDispatcher,
        IVideoCoordinator videoCoordinator,
        TimeProvider timeProvider,
        PairingClientViewModel? pairingClientPage = null,
        PairingServerViewModel? pairingServerViewModel = null)
        : base(uiThreadDispatcher, timeProvider: timeProvider)
    {
        this.trackCoordinator = trackCoordinator;
        this.videoCoordinator = videoCoordinator;
        Feedback = feedback;
        this.shell = shell;
        this.themeService = themeService;
        this.appEnvironment = appEnvironment;
        BikesPage = bikesPage;
        SessionsPage = sessionsPage;
        SetupsPage = setupsPage;
        LiveDaqsPage = liveDaqsPage;
        PairedDevicesPage = pairedDevicesPage;
        PairingClientPage = pairingClientPage;
        PairingServerViewModel = pairingServerViewModel;
        if (PairingServerViewModel is not null)
        {
            PairingServerViewModel.PropertyChanged += OnPairingServerPropertyChanged;
        }

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

        themeService.ThemeChanged += OnThemeChanged;

        SyncThemeState();
        SelectedLayoutProfile = appEnvironment.LayoutProfile;
        SyncLayoutProfileState();
        appEnvironment.PropertyChanged += OnAppEnvironmentPropertyChanged;

    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        SyncThemeState();
    }

    private void OnPairingServerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PairingServerViewModel.PairingPin))
        {
            OnPropertyChanged(nameof(IsPairingRequestVisible));
        }
    }

    #endregion Constructors

    #region Private methods

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

    #endregion

    #region Commands

    public IAsyncRelayCommand SyncCommand => Feedback.SyncCommand;
    public IAsyncRelayCommand OpenImportCommand => SessionsPage.OpenImportCommand;

    [RelayCommand]
    private void OpenDrawer()
    {
        IsDrawerOpen = true;
    }

    [RelayCommand]
    private void OpenClosePairedDevicesList()
    {
        if (!CanHostSyncServer)
        {
            return;
        }

        IsPairedDevicesListOpen = !IsPairedDevicesListOpen;
    }

    [RelayCommand]
    private void OpenPage(ViewModelBase view)
    {
        IsDrawerOpen = false;
        shell.Open(view);
    }

    private bool CanOpenGpsTracks() => CanImportGpsTracks;

    [RelayCommand(CanExecute = nameof(CanOpenGpsTracks))]
    private async Task OpenGpsTracks()
    {
        var result = await trackCoordinator.ImportGpxAsync();
        Feedback.ShowGpxResult(result);
    }

    private bool CanOpenVideo() => CanOpenVideos;

    [RelayCommand(CanExecute = nameof(CanOpenVideo))]
    private async Task OpenVideo()
    {
        var result = await videoCoordinator.ImportAsync();
        Feedback.ShowVideoResult(result);
    }

    [RelayCommand]
    private Task ChooseLayoutProfile(UiLayoutProfile profile) => shell.SwitchLayoutProfileAsync(profile);

    [RelayCommand]
    private async Task ToggleLayoutProfile()
    {
        await ChooseLayoutProfile(TargetLayoutProfile);
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

    private void SyncLayoutProfileState()
    {
        OnPropertyChanged(nameof(LayoutProfileMenuHeader));
        OnPropertyChanged(nameof(CompactLayoutProfileMenuText));
        OnPropertyChanged(nameof(WorkspaceLayoutProfileMenuText));
        OnPropertyChanged(nameof(TargetLayoutProfile));
        OnPropertyChanged(nameof(LayoutProfileActionMenuText));
    }

    private string FormatLayoutProfileMenuText(UiLayoutProfile profile)
    {
        var label = FormatLayoutProfile(profile);
        if (SelectedLayoutProfile == profile)
        {
            label += " (selected)";
        }

        return label;
    }

    private static string FormatLayoutProfile(UiLayoutProfile profile)
        => profile switch
        {
            UiLayoutProfile.Compact => "Compact",
            UiLayoutProfile.Workspace => "Workspace",
            _ => profile.ToString(),
        };

    private void OnAppEnvironmentPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IAppEnvironment.LayoutProfile))
        {
            SelectedLayoutProfile = appEnvironment.LayoutProfile;
            SyncLayoutProfileState();
        }
    }

    private static SufniThemeMode ResolveNextThemeMode(SufniThemeMode current, bool systemThemeAvailable)
        => current switch
        {
            SufniThemeMode.Dark => SufniThemeMode.Light,
            SufniThemeMode.Light when systemThemeAvailable => SufniThemeMode.System,
            SufniThemeMode.Light => SufniThemeMode.Dark,
            _ => SufniThemeMode.Dark
        };

}
