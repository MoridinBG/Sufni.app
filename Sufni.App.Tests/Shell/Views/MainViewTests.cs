using Microsoft.Extensions.Time.Testing;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;

using Sufni.App.Shell.Views;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Models;
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
    public async Task MainView_ShowsSyncBusyOverlay_WhenSyncRuns()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Compact);

        using var syncHarness = new Sufni.App.Tests.TestSupport.Sync.SyncTestServerHarness();
        var mainPages = MainPagesViewModelTestFactory.Create(syncCoordinator: syncHarness.CreateCoordinator(inbound: syncHarness.Inbound));
        var root = CreateRoot(mainPages);
        var view = new MainView
        {
            DataContext = root,
        };

        await using var mounted = await MountAsync(view);

        var overlay = mounted.View.FindControl<BusyOverlay>("SyncBusyOverlay");
        Assert.NotNull(overlay);
        Assert.False(overlay!.IsVisible);

        var runId = Guid.NewGuid();
        syncHarness.InboundActivity.OnNext(new PeerSyncActivity.Running("phone", runId,
            SynchronizationPhase.PullingRemoteChanges, 1, 6, syncHarness.Time.GetUtcNow()));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(overlay.IsVisible);

        syncHarness.InboundActivity.OnNext(new PeerSyncActivity.Completed(runId));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(overlay.IsVisible);
    }

    private static async Task<MountedView<MainView>> MountAsync(MainView view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedView<MainView>(host, view);
    }

    private static ShellRootViewModel CreateRoot(MainPagesViewModel mainPages)
    {
        var environment = new AppEnvironment(
            LayoutProfile: UiLayoutProfile.Compact,
            Capabilities: new AppCapabilities(
                CanHostSyncServer: false,
                CanPairAsClient: true,
                SupportsMassStorageImport: false,
                SupportsStorageProviderImport: true,
                CanPickFolder: true,
                CanPlayVideo: false),
            Input: new InputCapabilities(
                HasPointer: false,
                HasTouch: true,
                HasKeyboard: false,
                SupportsLongPressContextMenu: true,
                CommandModifier: CommandModifier.Control));

        return ShellRootViewModelTestFactory.Create(
            mainPages,
            new ShellWorkspaceViewModel(new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider()),
            environment,
            Substitute.For<IPlotZoomState>(),
            new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider());
    }

}
