using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.ExtensionHost.Contracts;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Tests.Views;
using Sufni.App.Stores;
using Sufni.App.Theming;
using Sufni.App.ViewModels.ItemLists;

namespace Sufni.App.Tests.ViewModels;

public class MainPagesViewModelTests
{
    private static readonly InlineUiThreadDispatcher UiThreadDispatcher = new();

    [Fact]
    public void SelectedIndex_ActivatesLivePage_WhenSelected_AndDeactivatesIt_WhenLeft()
    {
        var liveCoordinator = TestCoordinatorSubstitutes.LiveDaq();
        var livePage = new LiveDaqListViewModel(new LiveDaqStore(), liveCoordinator, UiThreadDispatcher);
        var viewModel = MainPagesViewModelTestFactory.Create(livePage);

        viewModel.SelectedIndex = 3;
        viewModel.SelectedIndex = 0;

        liveCoordinator.Received(1).Activate();
        liveCoordinator.Received(1).Deactivate();
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

        await viewModel.OpenGpsTracksCommand.ExecuteAsync(null);

        Assert.Single(viewModel.SessionsPage.Notifications);
    }

    [Fact]
    public void Constructor_ExposesNoExtensionToolbarActions_WhenNoProvidersAreRegistered()
    {
        var viewModel = MainPagesViewModelTestFactory.Create();

        Assert.Empty(viewModel.ExtensionToolbarActions);
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
            viewModel.ExtensionToolbarActions.Select(contribution => contribution.ContributionId));
        Assert.Same(dependency, viewModel.ExtensionToolbarActions[1].ViewModel);
    }

    [Fact]
    public void Constructor_ExposesExtensionToolbarActionsFromProviders()
    {
        var contribution = new AppToolbarContribution(
            "extension",
            "action",
            Order: 0,
            new TestContributionViewModel());

        var viewModel = MainPagesViewModelTestFactory.Create(
            appToolbarContributionProviders: [new TestAppToolbarContributionProvider(contribution)]);

        Assert.Equal([contribution], viewModel.ExtensionToolbarActions);
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

        public IReadOnlyList<AppToolbarContribution> CreateContributions()
        {
            return
            [
                new AppToolbarContribution("extension", "later", Order: 20, dependency),
            ];
        }
    }

    private sealed class EarlierToolbarContributionProvider : IAppToolbarContributionProvider
    {
        public string ExtensionId => "extension";

        public IReadOnlyList<AppToolbarContribution> CreateContributions()
        {
            return
            [
                new AppToolbarContribution("extension", "earlier", Order: 10, new TestContributionViewModel()),
            ];
        }
    }

    private sealed class MismatchedToolbarContributionProvider : IAppToolbarContributionProvider
    {
        public string ExtensionId => "owner";

        public IReadOnlyList<AppToolbarContribution> CreateContributions()
        {
            return
            [
                new AppToolbarContribution("other", "action", Order: 10, new TestContributionViewModel()),
            ];
        }
    }

    private sealed class DuplicateToolbarContributionProvider(string surface) : IAppToolbarContributionProvider
    {
        public string ExtensionId => "extension";

        public IReadOnlyList<AppToolbarContribution> CreateContributions()
        {
            return
            [
                new AppToolbarContribution(ExtensionId, "duplicate", Order: 10, new TestContributionViewModel()),
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
