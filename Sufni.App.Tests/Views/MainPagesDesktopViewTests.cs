using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.DesktopViews;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views;

public class MainPagesDesktopViewTests
{
    [AvaloniaFact]
    public async Task MainPagesDesktopView_ShowsPairingRequestPanel_WhenPairingPinExists()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var pairingCoordinator = Substitute.For<IPairingServerCoordinator>();
        pairingCoordinator.StartServerAsync().Returns(Task.CompletedTask);

        var pairingViewModel = new PairingServerViewModel(pairingCoordinator, new InlineUiThreadDispatcher())
        {
            PairingPin = "123456",
            RequestingDisplayName = "Phone",
            Remaining = 0.5,
        };

        var view = new MainPagesDesktopView
        {
            DataContext = MainPagesViewModelTestFactory.Create(pairingServerViewModel: pairingViewModel)
        };

        await using var mounted = await MountAsync(view);

        var pairingPanel = mounted.View.FindControl<Grid>("PairingRequestPanel");

        Assert.NotNull(pairingPanel);
        Assert.True(pairingPanel!.IsVisible);
    }

    [AvaloniaFact]
    public async Task MainPagesDesktopView_ShowsSyncInPairedDevicesSurface()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var pairingCoordinator = Substitute.For<IPairingServerCoordinator>();
        pairingCoordinator.StartServerAsync().Returns(Task.CompletedTask);
        var server = new TestSynchronizationServerService();
        var syncCoordinator = CreateSyncCoordinator(server);
        var viewModel = MainPagesViewModelTestFactory.Create(
            syncCoordinator: syncCoordinator,
            pairingServerViewModel: new PairingServerViewModel(pairingCoordinator, new InlineUiThreadDispatcher()));
        var view = new MainPagesDesktopView
        {
            DataContext = viewModel
        };

        await using var mounted = await MountAsync(view);

        var buttonSpinner = mounted.View.FindControl<ActivityIndicator>("PairedDevicesSyncIndicator");
        var panel = mounted.View.FindControl<Grid>("PairedDevicesPanel");
        var overlay = mounted.View.FindControl<BusyOverlay>("DesktopSyncBusyOverlay");
        Assert.NotNull(buttonSpinner);
        Assert.NotNull(panel);
        Assert.NotNull(overlay);

        var progress = new SynchronizationProgressSnapshot(
            SynchronizationPhase.ReceivingChanges,
            "Receiving remote changes",
            CurrentStep: 0,
            TotalSteps: 0,
            IsDeterminate: false);

        server.RaiseSyncActivityStarted(progress);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(viewModel.IsPairedDevicesListOpen);
        Assert.True(buttonSpinner!.IsVisible);
        Assert.True(buttonSpinner.IsActive);
        Assert.False(panel!.IsVisible);

        viewModel.IsPairedDevicesListOpen = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(panel.IsVisible);
        Assert.True(overlay!.IsVisible);
        Assert.True(overlay.IsActive);
        Assert.True(overlay.ShowProgress);
        Assert.False(overlay.IsProgressIndeterminate);
        Assert.Equal("Receiving remote changes", overlay.Message);
        Assert.Equal(1.0 / 6, overlay.ProgressValue, precision: 6);

        server.RaiseSyncActivityEnded(progress);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(buttonSpinner.IsVisible);
        Assert.False(overlay.IsVisible);
    }

    private static async Task<MountedMainPagesDesktopView> MountAsync(MainPagesDesktopView view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedMainPagesDesktopView(host, view);
    }

    private static SyncCoordinator CreateSyncCoordinator(ISynchronizationServerService server) =>
        new(
            Substitute.For<IBikeStoreWriter>(),
            Substitute.For<ISetupStoreWriter>(),
            Substitute.For<ISessionStoreWriter>(),
            Substitute.For<IRecordedSessionSourceStore>(),
            Substitute.For<IPairedDeviceStoreWriter>(),
            synchronizationClientService: null,
            pairingClientCoordinator: null,
            synchronizationServerService: server,
            backgroundTaskRunner: new InlineBackgroundTaskRunner(),
            inboundActivityIdleGrace: TimeSpan.Zero);
}

internal sealed class MountedMainPagesDesktopView(Window host, MainPagesDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public MainPagesDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
