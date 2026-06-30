using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.Tests.TestSupport;

using Sufni.App.Shell.Views;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.ViewModels;
using Sufni.App.SyncAndPairing.Stores;
namespace Sufni.App.Tests.Views;

[Collection("Ui")]
public class MainViewTests
{
    [AvaloniaFact]
    public async Task MainView_AttachesNavigationPageHost_WhenLoaded_AndDetachesWhenUnloaded()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var mainPages = MainPagesViewModelTestFactory.Create();
        var navigationHost = Substitute.For<IMobileNavigationShellHost>();
        var pageHost = Substitute.For<IMobileNavigationPageHost>();
        var viewModel = new MainViewModel(mainPages, navigationHost, new InlineUiThreadDispatcher());
        var view = new MainView
        {
            DataContext = viewModel,
        };
        view.SetNavigationPageHost(pageHost);

        var mounted = await MountAsync(view);
        var navigationPage = mounted.View.FindControl<NavigationPage>("RootNavigationPage");

        Assert.NotNull(navigationPage);
        pageHost.Received(1).Attach(navigationPage!);

        await mounted.DisposeAsync();

        pageHost.Received(1).Detach(navigationPage!);
    }

    [AvaloniaFact]
    public async Task MainView_ShowsSyncBusyOverlay_WhenSyncRuns()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var server = new TestSynchronizationServerService();
        var mainPages = MainPagesViewModelTestFactory.Create(syncCoordinator: CreateSyncCoordinator(server));
        var navigationHost = Substitute.For<IMobileNavigationShellHost>();
        var viewModel = new MainViewModel(mainPages, navigationHost, new InlineUiThreadDispatcher());
        var view = new MainView
        {
            DataContext = viewModel,
        };
        view.SetNavigationPageHost(Substitute.For<IMobileNavigationPageHost>());

        await using var mounted = await MountAsync(view);

        var overlay = mounted.View.FindControl<BusyOverlay>("SyncBusyOverlay");
        Assert.NotNull(overlay);
        Assert.False(overlay!.IsVisible);

        var progress = new SynchronizationProgressSnapshot(
            SynchronizationPhase.PullingRemoteChanges,
            "Pulling remote changes",
            CurrentStep: 3,
            TotalSteps: 8,
            IsDeterminate: true);

        server.RaiseSyncActivityStarted(progress);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(overlay.IsVisible);
        Assert.True(overlay.IsActive);
        Assert.True(overlay.ShowProgress);
        Assert.False(overlay.IsProgressIndeterminate);
        Assert.Equal("Pulling remote changes", overlay.Message);
        Assert.Equal(0.375, overlay.ProgressValue);

        server.RaiseSyncActivityEnded(progress);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(overlay.IsVisible);
        Assert.False(overlay.IsActive);
    }

    private static async Task<MountedMainView> MountAsync(MainView view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedMainView(host, view);
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
}

internal sealed class MountedMainView(Window host, MainView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public MainView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
