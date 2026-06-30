using System.Windows.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.ExtensionHost.Contracts;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.Services;
using Sufni.App.Tests.TestSupport;
using Sufni.App.Tests.Views;
using Sufni.App.Theming;

using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.LiveDaq.Stores;
using Sufni.App.LiveDaq.ViewModels.ItemLists;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.ViewModels;
using Sufni.App.SyncAndPairing.Stores;
namespace Sufni.App.Tests.ViewModels;

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

        await trackCoordinator.Received(1).ImportGpxAsync();
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
    public void OpenPageCommand_ClosesDrawerAndPushesNonPrimaryPageThroughShell()
    {
        var shell = Substitute.For<IShellCoordinator>();
        var viewModel = MainPagesViewModelTestFactory.Create(shell: shell);
        var page = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        viewModel.IsDrawerOpen = true;

        viewModel.OpenPageCommand.Execute(page);

        Assert.False(viewModel.IsDrawerOpen);
        shell.Received(1).Open(page);
    }

    [Fact]
    public void Constructor_ExposesNoExtensionToolbarActions_WhenNoProvidersAreRegistered()
    {
        var viewModel = MainPagesViewModelTestFactory.Create();

        Assert.Empty(viewModel.ExtensionToolbarCommands);
        Assert.Empty(viewModel.ExtensionToolbarViews);
    }

    [Fact]
    public void Constructor_CreatesExtensionToolbarActionsFromDiProviders_InOrder()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ToolbarDependency>();
        services.AddSingleton<IAppToolbarContributionProvider, LaterToolbarContributionProvider>();
        services.AddSingleton<IAppToolbarContributionProvider, EarlierToolbarContributionProvider>();
        using var provider = services.BuildServiceProvider();
        var dependency = provider.GetRequiredService<ToolbarDependency>();

        var viewModel = MainPagesViewModelTestFactory.Create(
            appToolbarContributionProviders: provider.GetServices<IAppToolbarContributionProvider>());

        Assert.Equal(
            ["earlier", "later"],
            viewModel.ExtensionToolbarViews.Select(contribution => contribution.ContributionId));
        Assert.Empty(viewModel.ExtensionToolbarCommands);
        Assert.Same(dependency, viewModel.ExtensionToolbarViews[1].ViewModel);
    }

    [Fact]
    public void Constructor_ExposesExtensionToolbarActionsFromProviders()
    {
        var command = Substitute.For<ICommand>();
        var commandContribution = new AppToolbarCommandContribution(
            "extension",
            "command",
            Order: 0,
            "Command",
            Icon: null,
            command);
        var viewContribution = new AppToolbarViewContribution(
            "extension",
            "view",
            Order: 1,
            new TestContributionViewModel());

        var viewModel = MainPagesViewModelTestFactory.Create(
            appToolbarContributionProviders:
            [
                new TestAppToolbarContributionProvider(
                    [commandContribution],
                    [viewContribution]),
            ]);

        Assert.Equal([commandContribution], viewModel.ExtensionToolbarCommands);
        Assert.Equal([viewContribution], viewModel.ExtensionToolbarViews);
    }

    [Fact]
    public void Constructor_RejectsExtensionToolbarContributionFromDifferentOwner()
    {
        var provider = new MismatchedToolbarContributionProvider();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MainPagesViewModelTestFactory.Create(appToolbarContributionProviders: [provider]));

        Assert.Contains("other:action", exception.Message);
        Assert.Contains("owner", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsDuplicateExtensionToolbarContributionIdsForSameExtension()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MainPagesViewModelTestFactory.Create(
                appToolbarContributionProviders:
                [
                    new DuplicateToolbarContributionProvider("first"),
                    new DuplicateToolbarContributionProvider("second"),
                ]));

        Assert.Contains("duplicate", exception.Message);
        Assert.Contains("extension", exception.Message);
    }

    [Fact]
    public async Task Constructor_RefreshesAppData_WithInitialDatabaseLoad()
    {
        var appDataRefresher = Substitute.For<IAppDataRefresher>();
        appDataRefresher.RefreshAsync().Returns(Task.CompletedTask);

        _ = MainPagesViewModelTestFactory.Create(appDataRefresher: appDataRefresher);

        await appDataRefresher.Received(1).RefreshAsync();
    }

    [Fact]
    public async Task Constructor_RefreshesExtensionStateParticipants_WithInitialDatabaseLoad()
    {
        var participant = new RecordingExtensionStateRefreshParticipant();

        _ = MainPagesViewModelTestFactory.Create(extensionStateRefreshParticipants: [participant]);

        await participant.Refreshed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, participant.RefreshCount);
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
            Substitute.For<IBikeStoreWriter>(),
            Substitute.For<ISetupStoreWriter>(),
            Substitute.For<ISessionStoreWriter>(),
            Substitute.For<IRecordedSessionSourceStoreWriter>(),
            Substitute.For<IPairedDeviceStoreWriter>(),
            synchronizationClientService: null,
            pairingClientCoordinator: null,
            synchronizationServerService: server,
            backgroundTaskRunner: new InlineBackgroundTaskRunner(),
            inboundActivityIdleGrace: TimeSpan.Zero);

    private sealed class ToolbarDependency : IAppToolbarContributionViewModel;

    private sealed class LaterToolbarContributionProvider(ToolbarDependency dependency) : IAppToolbarContributionProvider
    {
        public string ExtensionId => "extension";

        public IReadOnlyList<AppToolbarCommandContribution> CreateCommandContributions() => [];

        public IReadOnlyList<AppToolbarViewContribution> CreateViewContributions()
        {
            return
            [
                new AppToolbarViewContribution("extension", "later", Order: 20, dependency),
            ];
        }
    }

    private sealed class EarlierToolbarContributionProvider : IAppToolbarContributionProvider
    {
        public string ExtensionId => "extension";

        public IReadOnlyList<AppToolbarCommandContribution> CreateCommandContributions() => [];

        public IReadOnlyList<AppToolbarViewContribution> CreateViewContributions()
        {
            return
            [
                new AppToolbarViewContribution("extension", "earlier", Order: 10, new TestContributionViewModel()),
            ];
        }
    }

    private sealed class MismatchedToolbarContributionProvider : IAppToolbarContributionProvider
    {
        public string ExtensionId => "owner";

        public IReadOnlyList<AppToolbarCommandContribution> CreateCommandContributions()
        {
            return
            [
                new AppToolbarCommandContribution(
                    "other",
                    "action",
                    Order: 10,
                    "Action",
                    Icon: null,
                    Substitute.For<ICommand>()),
            ];
        }

        public IReadOnlyList<AppToolbarViewContribution> CreateViewContributions() => [];
    }

    private sealed class DuplicateToolbarContributionProvider(string surface) : IAppToolbarContributionProvider
    {
        public string ExtensionId => "extension";

        public IReadOnlyList<AppToolbarCommandContribution> CreateCommandContributions()
        {
            return
            [
                new AppToolbarCommandContribution(
                    ExtensionId,
                    "duplicate",
                    Order: 10,
                    "Duplicate",
                    Icon: null,
                    Substitute.For<ICommand>()),
            ];
        }

        public IReadOnlyList<AppToolbarViewContribution> CreateViewContributions()
        {
            return
            [
                new AppToolbarViewContribution(ExtensionId, "duplicate", Order: 20, new TestContributionViewModel()),
            ];
        }

        public override string ToString() => surface;
    }

    private sealed class RecordingExtensionStateRefreshParticipant : IExtensionStateRefreshParticipant
    {
        public TaskCompletionSource Refreshed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RefreshCount { get; private set; }

        public Task RefreshExtensionStateAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            Refreshed.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
