using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

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
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

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
    public async Task CompactShellView_SelectedPrimaryIndex_UpdatesTabbedPageSelection()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var root = CreateRoot();
        var view = new CompactShellView
        {
            DataContext = root,
        };

        await using var mounted = await MountAsync(view);

        var tabbedPage = mounted.View.FindControl<TabbedPage>("PagesTabbedPage");

        Assert.NotNull(tabbedPage);
        Assert.Equal(0, tabbedPage!.SelectedIndex);

        root.Pages.SelectedPrimaryIndex = 2;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(2, tabbedPage.SelectedIndex);
    }

    [AvaloniaFact]
    public async Task CompactShellView_BindsMainPagesAndPrimaryPageContent()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var root = CreateRoot();
        var view = new CompactShellView
        {
            DataContext = root,
        };

        await using var mounted = await MountAsync(view);

        var menuPanel = mounted.View.FindControl<SidePanel>("MenuPanel");
        var tabbedPage = mounted.View.FindControl<TabbedPage>("PagesTabbedPage")
            ?? throw new InvalidOperationException("Primary tabbed page was not found.");
        var pages = tabbedPage.Pages
            ?? throw new InvalidOperationException("Primary pages were not found.");
        var sessionsPage = pages.Cast<ContentPage>().First();

        Assert.Same(root.Pages, mounted.View.MainPages);
        Assert.Same(root.Pages, menuPanel!.DataContext);
        Assert.False(sessionsPage.AutomaticallyApplySafeAreaPadding);
        Assert.Equal("Sessions", sessionsPage.Header);
        Assert.Same(root.Pages.SessionsPage, sessionsPage.Content);
    }

    [AvaloniaFact]
    public async Task CompactShellView_SidePanel_UsesDirectLayoutProfileAction()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var uiPreferences = Substitute.For<IUiPreferences>();
        uiPreferences.SetLayoutProfileAsync(Arg.Any<UiLayoutProfile?>()).Returns(Task.CompletedTask);
        var environment = CreateMobileCompactEnvironment();
        var pages = MainPagesViewModelTestFactory.Create(
            appEnvironment: environment,
            uiPreferences: uiPreferences);
        var root = CreateRoot(pages, environment);
        var view = new CompactShellView
        {
            DataContext = root,
        };

        await using var mounted = await MountAsync(view);

        var menuPanel = mounted.View.FindControl<SidePanel>("MenuPanel")
            ?? throw new InvalidOperationException("Side panel was not found.");
        var menuItem = menuPanel.FindControl<MenuItem>("LayoutProfileMenuItem")
            ?? throw new InvalidOperationException("Layout profile menu item was not found.");

        Assert.Equal("workspace", menuItem.Header);
        Assert.Same(pages.ToggleLayoutProfileCommand, menuItem.Command);

        menuItem.Command!.Execute(menuItem.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(UiLayoutProfile.Workspace, pages.SelectedLayoutProfile);
        Assert.Equal("compact", menuItem.Header);
        await uiPreferences.Received(1).SetLayoutProfileAsync(UiLayoutProfile.Workspace);
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
        Assert.IsType<PairedDeviceListDesktopView>(
            new ViewLocator(environment).Build(pages.PairedDevicesPage));
    }

    [AvaloniaFact]
    public async Task CompactShellView_ShowsPairingRequestPanel_WhenDesktopHostCapabilityHasRequest()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Compact);

        var environment = CreateDesktopCompactEnvironment();
        var pairingServerCoordinator = Substitute.For<IPairingServerCoordinator>();
        pairingServerCoordinator.StartServerAsync().Returns(Task.CompletedTask);
        var pairingServer = new PairingServerViewModel(
            pairingServerCoordinator,
            new InlineUiThreadDispatcher())
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

        var pairingRequestPanel = mounted.View.FindControl<Grid>("CompactPairingRequestPanel")
            ?? throw new InvalidOperationException("Pairing request panel was not found.");

        Assert.True(pairingRequestPanel.IsVisible);
        await pairingServerCoordinator.Received(1).StartServerAsync();
    }

    private static ShellRootViewModel CreateRoot(
        MainPagesViewModel? pages = null,
        IAppEnvironment? environment = null)
    {
        environment ??= CreateMobileCompactEnvironment();
        pages ??= MainPagesViewModelTestFactory.Create(appEnvironment: environment);

        return new ShellRootViewModel(
            pages,
            new ShellWorkspaceViewModel(new InlineUiThreadDispatcher()),
            environment,
            Substitute.For<IPlotZoomState>(),
            new InlineUiThreadDispatcher());
    }

    private static IAppEnvironment CreateMobileCompactEnvironment() =>
        new AppEnvironment(
            DefaultLayoutProfile: UiLayoutProfile.Compact,
            LayoutProfile: UiLayoutProfile.Compact,
            Capabilities: new AppCapabilities(
                CanHostSyncServer: false,
                CanPairAsClient: true,
                HasHaptics: true,
                SupportsMassStorageImport: false,
                SupportsStorageProviderImport: true,
                SupportsNativeWindowing: false),
            Input: new InputCapabilities(
                HasPointer: false,
                HasTouch: true,
                HasKeyboard: false,
                SupportsPinch: true,
                SupportsLongPressContextMenu: true));

    private static IAppEnvironment CreateDesktopCompactEnvironment() =>
        MainPagesViewModelTestFactory.CreateAppEnvironment(UiLayoutProfile.Compact);

    private static async Task<MountedCompactShellView> MountAsync(CompactShellView view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedCompactShellView(host, view);
    }
}

internal sealed class MountedCompactShellView(Window host, CompactShellView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public CompactShellView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
