using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Theming;

using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.LiveDaq.Stores;
using Sufni.App.LiveDaq.ViewModels.ItemLists;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.ViewModels;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Video.Coordinators;
namespace Sufni.App.Tests.Shell.ViewModels;

[Collection("Ui")]
public class MainPagesViewModelTests
{
    private static readonly InlineUiThreadDispatcher UiThreadDispatcher = new();

    [Fact]
    public void SelectedPrimaryIndex_ActivatesLivePage_WhenSelected_AndDeactivatesIt_WhenLeft()
    {
        var liveCoordinator = TestCoordinatorSubstitutes.LiveDaq();
        var livePage = new LiveDaqListViewModel(new LiveDaqStore(), liveCoordinator, UiThreadDispatcher);
        var viewModel = MainPagesViewModelTestFactory.Create(livePage);
        var liveIndex = viewModel.PrimaryPages.IndexOf(
            viewModel.PrimaryPages.Single(page => page.Role == MainPrimaryPageRole.LiveDaqs));

        viewModel.SelectedPrimaryIndex = liveIndex;
        viewModel.SelectedPrimaryIndex = 0;

        liveCoordinator.Received(1).Activate();
        liveCoordinator.Received(1).Deactivate();
    }

    [Fact]
    public void Constructor_CreatesPrimaryPagesInExpectedOrder()
    {
        var viewModel = MainPagesViewModelTestFactory.Create();

        Assert.Equal(
            [MainPrimaryPageRole.Sessions, MainPrimaryPageRole.Setups, MainPrimaryPageRole.Bikes, MainPrimaryPageRole.LiveDaqs],
            viewModel.PrimaryPages.Select(page => page.Role));
        Assert.Equal(["Sessions", "Setups", "Bikes", "Live"], viewModel.PrimaryPages.Select(page => page.Header));
        Assert.Equal(
            ["/Assets/fa-chart-line.svg", "/Assets/cog.svg", "/Assets/fa-person-mountainbiking.svg", "/Assets/fa-link.svg"],
            viewModel.PrimaryPages.Select(page => page.IconPath));
        Assert.Same(viewModel.SessionsPage, viewModel.PrimaryPages[0].Content);
        Assert.Same(viewModel.SetupsPage, viewModel.PrimaryPages[1].Content);
        Assert.Same(viewModel.BikesPage, viewModel.PrimaryPages[2].Content);
        Assert.Same(viewModel.LiveDaqsPage, viewModel.PrimaryPages[3].Content);
    }

    [Fact]
    public async Task OpenGpsTracksCommand_ImportsGpxThroughTrackCoordinator()
    {
        var trackCoordinator = TestCoordinatorSubstitutes.Track();
        var viewModel = MainPagesViewModelTestFactory.Create(trackCoordinator: trackCoordinator);

        await viewModel.OpenGpsTracksCommand.ExecuteAsync(null);

        await trackCoordinator.Received(1).ImportGpxAsync(cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_ExposesShellActionsFromCapabilities()
    {
        var environment = MainPagesViewModelTestFactory.CreateAppEnvironment(
            capabilities: new AppCapabilities(
                CanHostSyncServer: false,
                CanPairAsClient: false,
                SupportsMassStorageImport: false,
                SupportsStorageProviderImport: false,
                CanPlayVideo: false));

        var viewModel = MainPagesViewModelTestFactory.Create(appEnvironment: environment);

        Assert.False(viewModel.CanHostSyncServer);
        Assert.False(viewModel.CanShowPairingClientActions);
        Assert.False(viewModel.CanImportSessions);
        Assert.False(viewModel.CanImportGpsTracks);
        Assert.False(viewModel.OpenImportCommand.CanExecute(null));
        Assert.False(viewModel.OpenGpsTracksCommand.CanExecute(null));
        Assert.False(viewModel.OpenVideoCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChooseLayoutProfileCommand_SavesLocalPreference_AndAppliesProfileLive()
    {
        var uiPreferences = Substitute.For<IUiPreferences>();
        uiPreferences.SetLayoutProfileAsync(Arg.Any<UiLayoutProfile?>()).Returns(Task.CompletedTask);
        var appEnvironment = MainPagesViewModelTestFactory.CreateAppEnvironment(UiLayoutProfile.Compact);
        var viewModel = MainPagesViewModelTestFactory.Create(
            appEnvironment: appEnvironment,
            uiPreferences: uiPreferences);

        await viewModel.ChooseLayoutProfileCommand.ExecuteAsync(UiLayoutProfile.Workspace);

        // The layout profile switches live: choosing a profile persists the preference and
        // applies it to the environment in the same step, rather than deferring to a restart.
        Assert.Equal(UiLayoutProfile.Workspace, viewModel.SelectedLayoutProfile);
        Assert.Equal(UiLayoutProfile.Workspace, appEnvironment.LayoutProfile);
        await uiPreferences.Received(1).SetLayoutProfileAsync(UiLayoutProfile.Workspace);
    }

    [Fact]
    public async Task ToggleLayoutProfileCommand_SavesTargetProfile()
    {
        var uiPreferences = Substitute.For<IUiPreferences>();
        uiPreferences.SetLayoutProfileAsync(Arg.Any<UiLayoutProfile?>()).Returns(Task.CompletedTask);
        var viewModel = MainPagesViewModelTestFactory.Create(
            appEnvironment: MainPagesViewModelTestFactory.CreateAppEnvironment(UiLayoutProfile.Compact),
            uiPreferences: uiPreferences);

        Assert.Equal(UiLayoutProfile.Workspace, viewModel.TargetLayoutProfile);
        Assert.Equal("workspace", viewModel.LayoutProfileActionMenuText);

        await viewModel.ToggleLayoutProfileCommand.ExecuteAsync(null);

        Assert.Equal(UiLayoutProfile.Workspace, viewModel.SelectedLayoutProfile);
        Assert.Equal(UiLayoutProfile.Compact, viewModel.TargetLayoutProfile);
        Assert.Equal("compact", viewModel.LayoutProfileActionMenuText);
        await uiPreferences.Received(1).SetLayoutProfileAsync(UiLayoutProfile.Workspace);
    }

    [Fact]
    public async Task ChooseLayoutProfileCommand_ToCompact_Cancels_WhenBackgroundDirtyTabCloseIsCancelled()
    {
        var uiPreferences = Substitute.For<IUiPreferences>();
        uiPreferences.SetLayoutProfileAsync(Arg.Any<UiLayoutProfile?>()).Returns(Task.CompletedTask);
        var appEnvironment = MainPagesViewModelTestFactory.CreateAppEnvironment(UiLayoutProfile.Workspace);
        var workspace = new ShellWorkspaceViewModel(UiThreadDispatcher);
        var shell = Substitute.For<IShellCoordinator>();
        var dialogService = Substitute.For<IDialogService>();
        dialogService.ShowCloseConfirmationAsync(Arg.Any<bool>()).Returns(PromptResult.Cancel);
        var backgroundTab = new ConfirmableTabPageViewModel(shell, dialogService, dirty: true);
        var currentTab = new ConfirmableTabPageViewModel(shell, dialogService, dirty: false);
        workspace.OpenOrFocus(backgroundTab);
        workspace.OpenOrFocus(currentTab);
        var viewModel = MainPagesViewModelTestFactory.Create(
            workspace: workspace,
            appEnvironment: appEnvironment,
            uiPreferences: uiPreferences);

        await viewModel.ChooseLayoutProfileCommand.ExecuteAsync(UiLayoutProfile.Compact);

        Assert.Equal(UiLayoutProfile.Workspace, viewModel.SelectedLayoutProfile);
        Assert.Equal(UiLayoutProfile.Workspace, appEnvironment.LayoutProfile);
        Assert.Same(currentTab, workspace.CurrentTab);
        Assert.Equal([backgroundTab, currentTab], workspace.Tabs);
        Assert.Equal(0, backgroundTab.CloseCount);
        await uiPreferences.DidNotReceive().SetLayoutProfileAsync(UiLayoutProfile.Compact);
    }

    [Fact]
    public async Task ChooseLayoutProfileCommand_ToCompact_ClosesConfirmedBackgroundTabs()
    {
        var uiPreferences = Substitute.For<IUiPreferences>();
        uiPreferences.SetLayoutProfileAsync(Arg.Any<UiLayoutProfile?>()).Returns(Task.CompletedTask);
        var appEnvironment = MainPagesViewModelTestFactory.CreateAppEnvironment(UiLayoutProfile.Workspace);
        var workspace = new ShellWorkspaceViewModel(UiThreadDispatcher);
        var shell = Substitute.For<IShellCoordinator>();
        var dialogService = Substitute.For<IDialogService>();
        dialogService.ShowCloseConfirmationAsync(Arg.Any<bool>()).Returns(PromptResult.No);
        var backgroundTab = new ConfirmableTabPageViewModel(shell, dialogService, dirty: true);
        var currentTab = new ConfirmableTabPageViewModel(shell, dialogService, dirty: false);
        workspace.OpenOrFocus(backgroundTab);
        workspace.OpenOrFocus(currentTab);
        var viewModel = MainPagesViewModelTestFactory.Create(
            workspace: workspace,
            appEnvironment: appEnvironment,
            uiPreferences: uiPreferences);

        await viewModel.ChooseLayoutProfileCommand.ExecuteAsync(UiLayoutProfile.Compact);

        Assert.Equal(UiLayoutProfile.Compact, viewModel.SelectedLayoutProfile);
        Assert.Equal(UiLayoutProfile.Compact, appEnvironment.LayoutProfile);
        Assert.Same(currentTab, workspace.CurrentTab);
        Assert.Equal([currentTab], workspace.Tabs);
        Assert.Equal(1, backgroundTab.ResetCount);
        Assert.Equal(1, backgroundTab.CloseCount);
        await uiPreferences.Received(1).SetLayoutProfileAsync(UiLayoutProfile.Compact);
    }

    [Fact]
    public async Task OpenGpsTracksCommand_AddsNotification_WhenGpxWasAlreadyImported()
    {
        var trackCoordinator = TestCoordinatorSubstitutes.Track();
        trackCoordinator.ImportGpxAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GpxImportResult(0, 1)));
        var viewModel = MainPagesViewModelTestFactory.Create(trackCoordinator: trackCoordinator);
        viewModel.SelectedPrimaryIndex = 2;

        await viewModel.OpenGpsTracksCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.SessionsPage.Notifications);
        Assert.Single(viewModel.BikesPage.Notifications);
    }

    [Fact]
    public async Task OpenVideoCommand_AddsNotificationToSelectedPage_WhenVideoIsLinked()
    {
        var videoCoordinator = Substitute.For<IVideoCoordinator>();
        videoCoordinator.ImportAsync(Arg.Any<CancellationToken>()).Returns(new VideoImportResult.Linked(2));
        var viewModel = MainPagesViewModelTestFactory.Create(videoCoordinator: videoCoordinator);
        viewModel.SelectedPrimaryIndex = 2;

        await viewModel.OpenVideoCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.SessionsPage.Notifications);
        Assert.Equal(["Linked video to 2 recorded sessions."], viewModel.BikesPage.Notifications);
    }

    [Fact]
    public async Task OpenVideoCommand_AddsErrorMessage_WhenImportFails()
    {
        var videoCoordinator = Substitute.For<IVideoCoordinator>();
        videoCoordinator.ImportAsync(Arg.Any<CancellationToken>()).Returns(new VideoImportResult.Failed("no sessions"));
        var viewModel = MainPagesViewModelTestFactory.Create(videoCoordinator: videoCoordinator);

        await viewModel.OpenVideoCommand.ExecuteAsync(null);

        Assert.Equal(["no sessions"], viewModel.SessionsPage.ErrorMessages);
    }

    [Fact]
    public void OpenPageCommand_ClosesDrawerAndPushesNonPrimaryPageThroughShell()
    {
        var shell = Substitute.For<IShellCoordinator>();
        var viewModel = MainPagesViewModelTestFactory.Create(shell: shell);
        var page = new TestTabPageViewModel();
        viewModel.IsDrawerOpen = true;

        viewModel.OpenPageCommand.Execute(page);

        Assert.False(viewModel.IsDrawerOpen);
        shell.Received(1).Open(page);
    }

    [Fact]
    public async Task Constructor_RefreshesAllState_WithInitialDatabaseLoad()
    {
        var appStateRefreshOrchestrator = Substitute.For<IAppStateRefreshOrchestrator>();
        appStateRefreshOrchestrator.RefreshAllStateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _ = MainPagesViewModelTestFactory.Create(appStateRefreshOrchestrator: appStateRefreshOrchestrator);

        await appStateRefreshOrchestrator.Received(1).RefreshAllStateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ThemeState_UsesTwoModeCycle_WhenSystemThemeIsUnavailable()
    {
        var themeService = Substitute.For<IThemeService>();
        themeService.Mode.Returns(SufniThemeMode.Light);
        themeService.EffectiveMode.Returns(SufniThemeMode.Light);
        themeService.IsSystemThemeAvailable.Returns(false);

        var viewModel = MainPagesViewModelTestFactory.Create(themeService: themeService);

        Assert.Equal(SufniThemeMode.Light, viewModel.CurrentThemeMode);
        Assert.Equal(SufniThemeMode.Light, viewModel.EffectiveThemeMode);
        Assert.False(viewModel.IsSystemThemeAvailable);
        Assert.Equal(SufniThemeMode.Dark, viewModel.NextThemeMode);
    }

    [Fact]
    public void ThemeState_IncludesSystemMode_WhenSystemThemeIsAvailable()
    {
        var themeService = Substitute.For<IThemeService>();
        themeService.Mode.Returns(SufniThemeMode.Light);
        themeService.EffectiveMode.Returns(SufniThemeMode.Light);
        themeService.IsSystemThemeAvailable.Returns(true);

        var viewModel = MainPagesViewModelTestFactory.Create(themeService: themeService);

        Assert.True(viewModel.IsSystemThemeAvailable);
        Assert.Equal(SufniThemeMode.System, viewModel.NextThemeMode);
    }

    [Fact]
    public void ThemeState_MirrorsEffectiveMode_WhenSystemThemeIsActive()
    {
        var themeService = Substitute.For<IThemeService>();
        themeService.Mode.Returns(SufniThemeMode.System);
        themeService.EffectiveMode.Returns(SufniThemeMode.Dark);
        themeService.IsSystemThemeAvailable.Returns(true);

        var viewModel = MainPagesViewModelTestFactory.Create(themeService: themeService);

        Assert.Equal(SufniThemeMode.System, viewModel.CurrentThemeMode);
        Assert.Equal(SufniThemeMode.Dark, viewModel.EffectiveThemeMode);
        Assert.Equal(SufniThemeMode.Dark, viewModel.NextThemeMode);
    }

    [AvaloniaFact]
    public async Task SyncProgressState_MirrorsCoordinatorProgress()
    {
        var server = new TestSynchronizationServerService();
        var syncCoordinator = CreateSyncCoordinator(server);
        var viewModel = MainPagesViewModelTestFactory.Create(syncCoordinator: syncCoordinator);
        var progress = new SynchronizationProgressSnapshot(
            SynchronizationPhase.ReceivingChanges,
            "Receiving remote changes",
            CurrentStep: 0,
            TotalSteps: 0,
            IsDeterminate: false);

        server.RaiseSyncActivityStarted(progress);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.True(viewModel.SyncInProgress);
        Assert.Equal("Receiving remote changes", viewModel.SyncProgressText);
        Assert.Equal(1.0 / 6, viewModel.SyncProgressValue, precision: 6);
        Assert.False(viewModel.SyncProgressIsIndeterminate);

        server.RaiseSyncActivityEnded(progress);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.False(viewModel.SyncInProgress);
        Assert.Equal(string.Empty, viewModel.SyncProgressText);
        Assert.Equal(0, viewModel.SyncProgressValue);
    }

    private static SyncCoordinator CreateSyncCoordinator(ISynchronizationServerService server) =>
        new(
            Substitute.For<IAppStateRefreshOrchestrator>(),
            synchronizationClientService: null,
            pairingClientCoordinator: null,
            synchronizationServerService: server,
            backgroundTaskRunner: new InlineBackgroundTaskRunner(),
            inboundActivityIdleGrace: TimeSpan.Zero);

    private sealed class ConfirmableTabPageViewModel : TabPageViewModelBase
    {
        private bool underlyingDirty;

        public ConfirmableTabPageViewModel(
            IShellCoordinator shell,
            IDialogService dialogService,
            bool dirty)
            : base(shell, dialogService, MainPagesViewModelTests.UiThreadDispatcher)
        {
            underlyingDirty = dirty;
            IsDirty = dirty;
        }

        public int CloseCount { get; private set; }
        public int ResetCount { get; private set; }

        protected override void EvaluateDirtiness()
        {
            IsDirty = underlyingDirty;
        }

        protected override Task ResetImplementation()
        {
            ResetCount++;
            underlyingDirty = false;
            return Task.CompletedTask;
        }

        protected override Task CloseImplementation()
        {
            CloseCount++;
            return Task.CompletedTask;
        }
    }
}
