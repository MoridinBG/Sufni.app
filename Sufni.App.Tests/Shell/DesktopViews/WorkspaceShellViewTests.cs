using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.DesktopViews;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Tests.Shell.ViewModels;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Shell.DesktopViews;

[Collection("Ui")]
public class WorkspaceShellViewTests
{
    private static readonly InlineUiThreadDispatcher TestDispatcher = new();

    [AvaloniaFact]
    public void ViewLocator_BuildsWorkspaceShellView_ForWorkspaceShellRoot()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var root = CreateRoot();
        var view = new ViewLocator().Build(root);

        Assert.IsType<WorkspaceShellView>(view);
    }

    [AvaloniaFact]
    public async Task WorkspaceShellView_BindsPagesAndShowsEmptyWorkspaceState_WhenNoTabIsSelected()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var root = CreateRoot();
        var view = new WorkspaceShellView
        {
            DataContext = root,
        };

        await using var mounted = await MountAsync(view);

        var pagesHost = mounted.View.FindControl<ContentControl>("PagesHost");
        var emptyState = mounted.View.FindControl<Border>("EmptyWorkspaceState");
        var tabControl = mounted.View.FindControl<TabStrip>("TabControl");

        Assert.Same(root.Pages, pagesHost!.Content);
        Assert.True(emptyState!.IsVisible);
        Assert.Empty(tabControl!.Items);
    }

    [AvaloniaFact]
    public async Task WorkspaceShellView_BindsTabsAndHidesEmptyWorkspaceState_WhenTabIsSelected()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var root = CreateRoot();
        var tab = new TestTabPage("Session");
        root.Workspace.OpenOrFocus(tab);
        var view = new WorkspaceShellView
        {
            DataContext = root,
        };

        await using var mounted = await MountAsync(view);

        var emptyState = mounted.View.FindControl<Border>("EmptyWorkspaceState");
        var tabControl = mounted.View.FindControl<TabStrip>("TabControl");

        Assert.False(emptyState!.IsVisible);
        Assert.Same(tab, tabControl!.SelectedItem);
        Assert.Equal([tab], tabControl.Items);
    }

    [AvaloniaFact]
    public async Task WorkspaceShellView_TabDragFeedback_FadesDraggedTabAndShowsInsertionIndicator()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        await using var mounted = await MountAsync(new WorkspaceShellView
        {
            DataContext = CreateRoot(),
        });

        var tabItem = new TabStripItem();

        mounted.View.BeginTabDragFeedback(tabItem);
        mounted.View.ShowTabDropIndicator(120);

        Assert.True(mounted.View.IsTabDragFeedbackVisible);
        Assert.True(tabItem.Opacity < 1);
        Assert.True(mounted.View.IsTabDropIndicatorVisible);
        Assert.True(mounted.View.TabDropIndicatorX > 0);

        mounted.View.EndTabDragFeedback();

        Assert.False(mounted.View.IsTabDragFeedbackVisible);
        Assert.Equal(1, tabItem.Opacity);
        Assert.False(mounted.View.IsTabDropIndicatorVisible);
    }

    [AvaloniaFact]
    public async Task WorkspaceShellView_TabClose_NoopsAutomationPeerRefresh_WhenNoPanelPeerWasMaterialized()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);
        EnsureFluentTheme();

        var root = CreateRoot();
        var tab = new TestTabPage("Session");
        root.Workspace.OpenOrFocus(tab);
        var view = new WorkspaceShellView
        {
            DataContext = root,
        };

        await using var mounted = await MountAsync(view);

        root.Workspace.CloseTab(tab, rememberForRestore: false);
        await ViewTestHelpers.FlushDispatcherAsync();

        GC.KeepAlive(mounted.View);
    }

    [AvaloniaFact]
    public async Task WorkspaceShellView_TabClose_RefreshesPanelPeers_WhenPanelPeerChildrenWereCached()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);
        EnsureFluentTheme();

        var root = CreateRoot();
        var view = new WorkspaceShellView
        {
            DataContext = root,
        };

        await using var mounted = await MountAsync(view);

        OpenTabsMaterializePanelPeersAndCloseTarget(view, root.Workspace);

        await ViewTestHelpers.FlushDispatcherAsync();

        GC.KeepAlive(view);
        GC.KeepAlive(root);
    }

    private static void OpenTabsMaterializePanelPeersAndCloseTarget(
        WorkspaceShellView view,
        ShellWorkspaceViewModel workspace)
    {
        var target = new TestTabPage("Closed");
        var remaining = new TestTabPage("Remaining");
        workspace.OpenOrFocus(target);
        workspace.OpenOrFocus(remaining);

        FlushDispatcherSynchronously();
        view.Measure(new Size(900, 700));
        view.Arrange(new Rect(0, 0, 900, 700));
        view.UpdateLayout();
        FlushDispatcherSynchronously();

        var rootPeer = Assert.IsAssignableFrom<ControlAutomationPeer>(
            ControlAutomationPeer.CreatePeerForElement(Assert.IsAssignableFrom<Control>(TopLevel.GetTopLevel(view))));

        var tabContentHost = view.FindControl<ItemsControl>("TabContentHost");
        var contentContainer = Assert.Single(
            tabContentHost!.GetVisualDescendants().OfType<ContentPresenter>(),
            presenter => ReferenceEquals(presenter.DataContext, target) &&
                         presenter.GetVisualParent() is Panel);
        var contentPanel = Assert.IsAssignableFrom<Panel>(contentContainer.GetVisualParent());
        var contentPanelPeer = rootPeer.GetOrCreate(contentPanel);
        contentPanelPeer.GetParent();
        contentPanelPeer.GetChildren();

        var tabControl = view.FindControl<TabStrip>("TabControl");
        var tabStripItem = Assert.Single(
            tabControl!.GetVisualDescendants().OfType<TabStripItem>(),
            item => ReferenceEquals(item.DataContext, target));
        var tabStripPanel = Assert.IsAssignableFrom<Panel>(tabStripItem.GetVisualParent());
        var tabStripPanelPeer = rootPeer.GetOrCreate(tabStripPanel);
        tabStripPanelPeer.GetParent();
        tabStripPanelPeer.GetChildren();

        workspace.CloseTab(target, rememberForRestore: false);
    }

    private static void FlushDispatcherSynchronously()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            return;
        }

        Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetAwaiter().GetResult();
    }

    private static void EnsureFluentTheme()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");

        if (!application.Styles.OfType<FluentTheme>().Any())
        {
            application.Styles.Add(new FluentTheme());
        }
    }

    private static ShellRootViewModel CreateRoot()
    {
        var environment = new AppEnvironment(
            DefaultLayoutProfile: UiLayoutProfile.Workspace,
            LayoutProfile: UiLayoutProfile.Workspace,
            Capabilities: new AppCapabilities(
                CanHostSyncServer: true,
                CanPairAsClient: true,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true),
            Input: new InputCapabilities(
                HasPointer: true,
                HasTouch: false,
                HasKeyboard: true,
                SupportsLongPressContextMenu: false));

        return new ShellRootViewModel(
            MainPagesViewModelTestFactory.Create(),
            new ShellWorkspaceViewModel(TestDispatcher),
            environment,
            Substitute.For<IPlotZoomState>(),
            TestDispatcher);
    }

    private static async Task<MountedWorkspaceShellView> MountAsync(WorkspaceShellView view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedWorkspaceShellView(host, view);
    }

    private sealed class TestTabPage : TabPageViewModelBase
    {
        public TestTabPage(string name)
            : base(TestDispatcher)
        {
            Name = name;
        }
    }
}

internal sealed class MountedWorkspaceShellView(Window host, WorkspaceShellView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public WorkspaceShellView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
