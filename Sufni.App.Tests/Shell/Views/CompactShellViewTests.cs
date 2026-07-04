using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Shell.Views;
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

    private static ShellRootViewModel CreateRoot()
    {
        var environment = new AppEnvironment(
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

        return new ShellRootViewModel(
            MainPagesViewModelTestFactory.Create(),
            new ShellWorkspaceViewModel(new InlineUiThreadDispatcher()),
            environment,
            Substitute.For<IPlotZoomState>(),
            new InlineUiThreadDispatcher());
    }

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
