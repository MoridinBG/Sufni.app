using Microsoft.Extensions.Time.Testing;
using System.Threading.Tasks;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;

using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.DesktopViews;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.SyncAndPairing.ViewModels;
using Sufni.App.Tests.Shell.ViewModels;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Shell.DesktopViews;

[Collection("Ui")]
public class MainPagesDesktopViewTests
{
    [AvaloniaFact]
    public async Task MainPagesDesktopView_ShowsPairingRequestPanel_WhenPairingPinExists()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);

        var pairingCoordinator = Substitute.For<IInboundSyncCoordinator>();

        var pairingViewModel = new PairingServerViewModel(pairingCoordinator, new InlineUiThreadDispatcher(), new FakeTimeProvider(), new ManualUiTimerScheduler())
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

        var pairingPanel = mounted.View.FindControl<ContentControl>("PairingRequestPanel");

        Assert.NotNull(pairingPanel);
        Assert.True(pairingPanel!.IsVisible);

        var pinText = mounted.View.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Name == "DesktopPairingPinText");
        Assert.True(pinText.Bounds.Height > 24);
    }

    [AvaloniaFact]
    public async Task MainPagesDesktopView_HidesServerSyncSurfaces_WhenHostCapabilityIsUnavailable()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);

        var pairingCoordinator = Substitute.For<IInboundSyncCoordinator>();
        var pairingViewModel = new PairingServerViewModel(pairingCoordinator, new InlineUiThreadDispatcher(), new FakeTimeProvider(), new ManualUiTimerScheduler())
        {
            PairingPin = "123456",
            RequestingDisplayName = "Phone",
            Remaining = 0.5,
        };
        var environment = MainPagesViewModelTestFactory.CreateAppEnvironment(
            capabilities: new AppCapabilities(
                CanHostSyncServer: false,
                CanPairAsClient: false,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true,
                CanPickFolder: true,
                CanPlayVideo: false));
        var viewModel = MainPagesViewModelTestFactory.Create(
            appEnvironment: environment,
            pairingServerViewModel: pairingViewModel);
        viewModel.IsPairedDevicesListOpen = true;
        var view = new MainPagesDesktopView
        {
            DataContext = viewModel
        };

        await using var mounted = await MountAsync(view);

        Assert.False(mounted.View.FindControl<Button>("PairedDevicesButton")!.IsVisible);
        Assert.False(mounted.View.FindControl<Grid>("PairedDevicesPanel")!.IsVisible);
        Assert.False(mounted.View.FindControl<ContentControl>("PairingRequestPanel")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task MainPagesDesktopView_ShowsSyncInPairedDevicesSurface()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);

        var pairingCoordinator = Substitute.For<IInboundSyncCoordinator>();
        using var syncHarness = new Sufni.App.Tests.TestSupport.Sync.SyncTestServerHarness();
        var syncCoordinator = syncHarness.CreateCoordinator(inbound: syncHarness.Inbound);
        var viewModel = MainPagesViewModelTestFactory.Create(
            syncCoordinator: syncCoordinator,
            pairingServerViewModel: new PairingServerViewModel(pairingCoordinator, new InlineUiThreadDispatcher(), new FakeTimeProvider(), new ManualUiTimerScheduler()));
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

        var runId = Guid.NewGuid();
        syncHarness.InboundActivity.OnNext(new PeerSyncActivity.Running("phone", runId,
            SynchronizationPhase.PullingRemoteChanges, 1, 6, syncHarness.Time.GetUtcNow()));
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
        Assert.Equal("Serving remote changes", overlay.Message);
        Assert.Equal(1.0 / 6, overlay.ProgressValue, precision: 6);

        syncHarness.InboundActivity.OnNext(new PeerSyncActivity.Completed(runId));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(buttonSpinner.IsVisible);
        Assert.False(overlay.IsVisible);
    }

    [AvaloniaFact]
    public async Task MainPagesDesktopView_ShowsClientPairAction_WhenClientIsUnpaired()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);

        var pairingClientPage = CreatePairingClientPage(isPaired: false);
        var viewModel = MainPagesViewModelTestFactory.Create(
            appEnvironment: CreateClientEnvironment(),
            pairingClientPage: pairingClientPage);
        var view = new MainPagesDesktopView
        {
            DataContext = viewModel
        };

        await using var mounted = await MountAsync(view);

        var pairButton = mounted.View.FindControl<Button>("ClientPairButton");
        var syncButton = mounted.View.FindControl<Button>("ClientSyncButton");
        var unpairButton = mounted.View.FindControl<Button>("ClientUnpairButton");

        Assert.NotNull(pairButton);
        Assert.NotNull(syncButton);
        Assert.NotNull(unpairButton);
        Assert.True(pairButton!.IsVisible);
        Assert.False(syncButton!.IsVisible);
        Assert.False(unpairButton!.IsVisible);
        Assert.Same(viewModel.OpenPageCommand, pairButton.Command);
        Assert.Same(pairingClientPage, pairButton.CommandParameter);
    }

    [AvaloniaFact]
    public async Task MainPagesDesktopView_ShowsClientSyncAndUnpairActions_WhenClientIsPaired()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);

        var syncCoordinator = TestCoordinatorSubstitutes.Sync(new SyncState(PairingClientState.Paired, SyncActivity.Idle));
        var pairingClientPage = CreatePairingClientPage(isPaired: true);
        var viewModel = MainPagesViewModelTestFactory.Create(
            appEnvironment: CreateClientEnvironment(),
            syncCoordinator: syncCoordinator,
            pairingClientPage: pairingClientPage);
        var view = new MainPagesDesktopView
        {
            DataContext = viewModel
        };

        await using var mounted = await MountAsync(view);

        var pairButton = mounted.View.FindControl<Button>("ClientPairButton");
        var syncButton = mounted.View.FindControl<Button>("ClientSyncButton");
        var unpairButton = mounted.View.FindControl<Button>("ClientUnpairButton");

        Assert.NotNull(pairButton);
        Assert.NotNull(syncButton);
        Assert.NotNull(unpairButton);
        Assert.False(pairButton!.IsVisible);
        Assert.True(syncButton!.IsVisible);
        Assert.True(unpairButton!.IsVisible);
        Assert.Same(viewModel.SyncCommand, syncButton.Command);
        Assert.Same(viewModel.OpenPageCommand, unpairButton.Command);
        Assert.Same(pairingClientPage, unpairButton.CommandParameter);
    }

    [AvaloniaFact]
    public async Task MainPagesDesktopView_HidesOpenVideoButton_WhenVideoPlaybackIsUnavailable()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);

        var environment = MainPagesViewModelTestFactory.CreateAppEnvironment(
            capabilities: new AppCapabilities(
                CanHostSyncServer: true,
                CanPairAsClient: false,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true,
                CanPickFolder: true,
                CanPlayVideo: false));
        var view = new MainPagesDesktopView
        {
            DataContext = MainPagesViewModelTestFactory.Create(appEnvironment: environment)
        };

        await using var mounted = await MountAsync(view);

        Assert.False(mounted.View.FindControl<Button>("OpenVideoButton")!.IsVisible);
    }

    private static async Task<MountedMainPagesDesktopView> MountAsync(MainPagesDesktopView view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedMainPagesDesktopView(host, view);
    }

    private static IAppEnvironment CreateClientEnvironment() =>
        MainPagesViewModelTestFactory.CreateAppEnvironment(
            capabilities: new AppCapabilities(
                CanHostSyncServer: false,
                CanPairAsClient: true,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true,
                CanPickFolder: true,
                CanPlayVideo: false));

    private static PairingClientViewModel CreatePairingClientPage(bool isPaired)
    {
        var pairingClientCoordinator = Substitute.For<IPairingClientCoordinator>();
        pairingClientCoordinator.DisplayName.Returns("Phone");
        pairingClientCoordinator.State.Returns(isPaired ? PairingClientState.Paired : PairingClientState.Unpaired);

        return new PairingClientViewModel(
            pairingClientCoordinator,
            TestCoordinatorSubstitutes.Sync(new SyncState(pairingClientCoordinator.State, SyncActivity.Idle)),
            Substitute.For<IShellCoordinator>(),
            Substitute.For<IDialogService>(),
            new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider());
    }


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
