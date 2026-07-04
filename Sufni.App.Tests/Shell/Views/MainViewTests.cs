using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;

using Sufni.App.Shell.Views;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Infrastructure;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Tests.Shell.ViewModels;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Shell.Views;

[Collection("Ui")]
public class MainViewTests
{
    [AvaloniaFact]
    public void ViewLocator_BuildsCompactShellView_ForCompactShellRoot()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var root = CreateRoot(MainPagesViewModelTestFactory.Create());
        var view = new ViewLocator().Build(root);

        Assert.IsType<CompactShellView>(view);
    }

    [AvaloniaFact]
    public async Task MainView_UsesWorkspaceHost_WhenLoaded()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var root = CreateRoot(MainPagesViewModelTestFactory.Create());
        var view = new MainView
        {
            DataContext = root,
        };

        await using var mounted = await MountAsync(view);
        var workspaceHost = mounted.View.FindControl<ContentControl>("WorkspaceRootHost");

        Assert.NotNull(workspaceHost);
        Assert.True(workspaceHost!.IsVisible);
    }

    [AvaloniaFact]
    public async Task MainView_ShowsSyncBusyOverlay_WhenSyncRuns()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var server = new TestSynchronizationServerService();
        var mainPages = MainPagesViewModelTestFactory.Create(syncCoordinator: CreateSyncCoordinator(server));
        var root = CreateRoot(mainPages);
        var view = new MainView
        {
            DataContext = root,
        };

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

    private static ShellRootViewModel CreateRoot(MainPagesViewModel mainPages)
    {
        var environment = new AppEnvironment(
            DefaultLayoutProfile: UiLayoutProfile.Compact,
            LayoutProfile: UiLayoutProfile.Compact,
            Capabilities: new AppCapabilities(
                CanHostSyncServer: false,
                CanPairAsClient: true,
                SupportsMassStorageImport: false,
                SupportsStorageProviderImport: true),
            Input: new InputCapabilities(
                HasPointer: false,
                HasTouch: true,
                HasKeyboard: false,
                SupportsLongPressContextMenu: true));

        return new ShellRootViewModel(
            mainPages,
            new ShellWorkspaceViewModel(new InlineUiThreadDispatcher()),
            environment,
            Substitute.For<IPlotZoomState>(),
            new InlineUiThreadDispatcher());
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
