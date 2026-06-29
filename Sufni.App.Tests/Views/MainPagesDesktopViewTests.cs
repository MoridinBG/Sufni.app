using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Svg.Skia;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.DesktopViews;
using Sufni.App.ExtensionHost.Contracts;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views;

[Collection("Ui")]
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

    [AvaloniaFact]
    public async Task MainPagesDesktopView_RendersExtensionToolbarActions()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var command = Substitute.For<ICommand>();
        var commandContribution = new AppToolbarCommandContribution(
            "extension",
            "toolbar-command",
            Order: 0,
            "Desktop command",
            new ToolbarIconDescriptor("/Assets/fa-link.svg", Width: 17, Height: 19),
            command);
        var contributionViewModel = new TestContributionViewModel
        {
            Content = new TextBlock { Name = "DesktopExtensionToolbarAction", Text = "Desktop action" },
        };
        var contribution = new AppToolbarViewContribution(
            "extension",
            "toolbar-action",
            Order: 1,
            contributionViewModel);
        var view = new MainPagesDesktopView
        {
            DataContext = MainPagesViewModelTestFactory.Create(
                appToolbarContributionProviders:
                [
                    new TestAppToolbarContributionProvider(
                        [commandContribution],
                        [contribution]),
                ])
        };

        await using var mounted = await MountAsync(view);

        var host = Assert.Single(mounted.View.GetVisualDescendants().OfType<AppToolbarContributionsView>());
        var contributionsHost = host.FindControl<StackPanel>("ContributionsHost");
        Assert.NotNull(contributionsHost);

        // Command contributions render as icon-only embedded rail buttons with the label as a tooltip.
        var button = Assert.Single(contributionsHost!.Children.OfType<Button>());
        Assert.Contains("embedded", button.Classes);
        Assert.Same(command, button.Command);
        Assert.Equal("Desktop command", ToolTip.GetTip(button));
        var icon = Assert.IsType<Image>(button.Content);
        Assert.Equal(17, icon.Width);
        Assert.Equal(19, icon.Height);
        var svgImage = Assert.IsType<SvgImage>(icon.Source);
        Assert.NotNull(svgImage.Source?.Picture);

        // View contributions render directly in the host so their template resolves through ViewLocator.
        Assert.Contains(contributionViewModel, contributionsHost.Children);
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
            Substitute.For<IRecordedSessionSourceStoreWriter>(),
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
