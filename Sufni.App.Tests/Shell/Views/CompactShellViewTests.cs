using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Shell.Views.Controls;
using Sufni.App.Shell.Coordinators;
using Microsoft.Extensions.Time.Testing;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Shell.Views;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.DesktopViews.ItemLists;
using Sufni.App.SyncAndPairing.ViewModels;
using Sufni.App.Tests.Shell.ViewModels;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Shell.Views;

[Collection("Ui")]
public class CompactShellViewTests
{
    [AvaloniaFact]
    public async Task CompactShellView_OpensDrawer_WhenPagesCommandExecutes()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Compact);

        var root = CreateRoot();
        var view = new CompactShellView
        {
            DataContext = root,
        };

        await using var mounted = await MountAsync(view);

        var drawerPage = mounted.View.FindControl<DrawerPage>("MainDrawerPage");

        Assert.NotNull(drawerPage);
        Assert.False(drawerPage!.IsOpen);

        root.Pages.OpenDrawerCommand.Execute(null);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(root.Pages.IsDrawerOpen);
        Assert.True(drawerPage.IsOpen);
    }

    [AvaloniaFact]
    public async Task CompactShellView_TabbedPage_UpdatesSelectedPrimaryIndex_OnSelectionChange()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Compact);

        var root = CreateRoot();
        var view = new CompactShellView
        {
            DataContext = root,
        };

        await using var mounted = await MountAsync(view);

        var tabbedPage = mounted.View.FindControl<TabbedPage>("PagesTabbedPage");

        Assert.NotNull(tabbedPage);
        Assert.Equal(0, tabbedPage!.SelectedIndex);

        tabbedPage.SelectedIndex = 2;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(2, root.Pages.SelectedPrimaryIndex);

        tabbedPage.SelectedIndex = 3;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(3, root.Pages.SelectedPrimaryIndex);
    }

    [AvaloniaFact]
    public async Task CompactShellView_ShowsPairedDevicesPanel_WhenDesktopHostCapabilityIsAvailable()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Compact);

        var environment = CreateDesktopCompactEnvironment();
        var pages = MainPagesViewModelTestFactory.Create(appEnvironment: environment);
        var root = CreateRoot(pages, environment);
        var view = new CompactShellView
        {
            DataContext = root,
        };

        await using var mounted = await MountAsync(view);

        var menuPanel = mounted.View.FindControl<SidePanel>("MenuPanel")
            ?? throw new InvalidOperationException("Side panel was not found.");
        var menuItem = menuPanel.FindControl<MenuItem>("PairedDevicesMenuItem")
            ?? throw new InvalidOperationException("Paired devices menu item was not found.");
        var pairedDevicesPanel = mounted.View.FindControl<Grid>("CompactPairedDevicesPanel")
            ?? throw new InvalidOperationException("Paired devices panel was not found.");

        Assert.True(menuItem.IsVisible);
        Assert.False(pairedDevicesPanel.IsVisible);

        menuItem.Command!.Execute(null);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(pages.IsPairedDevicesListOpen);
        Assert.True(pairedDevicesPanel.IsVisible);
        menuItem.Command!.Execute(null);
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.False(pairedDevicesPanel.IsVisible);
    }

    [AvaloniaFact]
    public async Task CompactShellView_ShowsPairingRequestPanel_WhenDesktopHostCapabilityHasRequest()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Compact);

        var environment = CreateDesktopCompactEnvironment();
        var pairingServerCoordinator = Substitute.For<IInboundSyncCoordinator>();
        var pairingServer = new PairingServerViewModel(
            pairingServerCoordinator,
            new InlineUiThreadDispatcher(), new FakeTimeProvider(), new ManualUiTimerScheduler())
        {
            PairingPin = "123456",
            RequestingId = "test-device",
            Remaining = 0.5,
        };
        var pages = MainPagesViewModelTestFactory.Create(
            appEnvironment: environment,
            pairingServerViewModel: pairingServer);
        var root = CreateRoot(pages, environment);
        var view = new CompactShellView
        {
            DataContext = root,
        };

        await using var mounted = await MountAsync(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        var pairingRequestPanel = mounted.View.FindControl<ContentControl>("CompactPairingRequestPanel")
            ?? throw new InvalidOperationException("Pairing request panel was not found.");

        Assert.True(pairingRequestPanel.IsVisible);

        pairingServer.PairingPin = null;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.False(pairingRequestPanel.IsVisible);
    }

    private static ShellRootViewModel CreateRoot(
        MainPagesViewModel? pages = null,
        IAppEnvironment? environment = null)
    {
        environment ??= CreateMobileCompactEnvironment();
        pages ??= MainPagesViewModelTestFactory.Create(appEnvironment: environment);

        return ShellRootViewModelTestFactory.Create(
            pages,
            new ShellWorkspaceViewModel(new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider()),
            environment,
            Substitute.For<IPlotZoomState>(),
            new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider());
    }

    private static IAppEnvironment CreateMobileCompactEnvironment() =>
        new AppEnvironment(
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

    private static IAppEnvironment CreateDesktopCompactEnvironment() =>
        MainPagesViewModelTestFactory.CreateAppEnvironment(UiLayoutProfile.Compact);

    private static async Task<MountedView<CompactShellView>> MountAsync(CompactShellView view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedView<CompactShellView>(host, view);
    }
}
