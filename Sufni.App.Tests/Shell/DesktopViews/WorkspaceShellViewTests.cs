using Microsoft.Extensions.Time.Testing;
using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.DesktopViews;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.Shell.ViewModels;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Shell.DesktopViews;

[Collection("Ui")]
public class WorkspaceShellViewTests
{
    private static readonly InlineUiThreadDispatcher TestDispatcher = new();

    [AvaloniaFact]
    public async Task MiddleClick_PromptsOnlyOnceAfterRecreatingTheWorkspaceView()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);
        EnsureFluentTheme();
        await using (var previous = await MountAsync(new WorkspaceShellView { DataContext = CreateRoot() })) { }
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowCloseConfirmationAsync(Arg.Any<bool>()).Returns(PromptResult.Cancel);
        var tab = new TestTabPageViewModel(dialogs) { Name = "Unsaved", IsDirty = true };
        var root = CreateRoot();
        root.Workspace.OpenOrFocus(tab);
        await using var mounted = await MountAsync(new WorkspaceShellView { DataContext = root });
        var item = Assert.Single(mounted.View.GetVisualDescendants().OfType<TabStripItem>());
        var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), mounted.Host)!.Value;

        mounted.Host.MouseDown(point, MouseButton.Middle, RawInputModifiers.None);
        mounted.Host.MouseUp(point, MouseButton.Middle, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();

        await dialogs.Received(1).ShowCloseConfirmationAsync(Arg.Any<bool>());
        Assert.Same(tab, root.Workspace.CurrentTab);
    }

    [AvaloniaFact]
    public void ViewLocator_BuildsWorkspaceShellView_ForWorkspaceShellRoot()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var root = CreateRoot();
        var view = new ViewLocator().Build(root.Presentation);

        Assert.IsType<WorkspaceShellView>(view);
    }

    [AvaloniaFact]
    public async Task WorkspaceShellView_BindsPagesAndShowsEmptyWorkspaceState_WhenNoTabIsSelected()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);

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
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);

        var root = CreateRoot();
        var tab = new TestTabPageViewModel { Name = "Session" };
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
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);

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
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);
        EnsureFluentTheme();

        var root = CreateRoot();
        var tab = new TestTabPageViewModel { Name = "Session" };
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
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);
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

    [AvaloniaFact]
    public async Task WorkspaceShellView_TabClose_ClearsTabOnceActiveElement_FromClosedContent()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);
        EnsureFluentTheme();

        var root = CreateRoot();
        var closed = new TestTabPageViewModel { Name = "Closed" };
        var remaining = new TestTabPageViewModel { Name = "Remaining" };
        root.Workspace.OpenOrFocus(closed);
        root.Workspace.OpenOrFocus(remaining);
        var view = new WorkspaceShellView
        {
            DataContext = root,
        };

        await using var mounted = await MountAsync(view);

        var tabContentHost = view.FindControl<ItemsControl>("TabContentHost")!;
        var closedContainer = FindTabContentContainer(tabContentHost, closed);
        var cachedElement = Assert.IsAssignableFrom<InputElement>(
            closedContainer.GetVisualDescendants().OfType<Control>().First());
        KeyboardNavigation.SetTabOnceActiveElement(tabContentHost, cachedElement);

        root.Workspace.CloseTab(closed, rememberForRestore: false);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Null(KeyboardNavigation.GetTabOnceActiveElement(tabContentHost));
    }

    [AvaloniaFact]
    public async Task WorkspaceShellView_TabClose_PreservesTabOnceActiveElement_FromRemainingContent()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);
        EnsureFluentTheme();

        var root = CreateRoot();
        var closed = new TestTabPageViewModel { Name = "Closed" };
        var remaining = new TestTabPageViewModel { Name = "Remaining" };
        root.Workspace.OpenOrFocus(closed);
        root.Workspace.OpenOrFocus(remaining);
        var view = new WorkspaceShellView
        {
            DataContext = root,
        };

        await using var mounted = await MountAsync(view);

        var tabContentHost = view.FindControl<ItemsControl>("TabContentHost")!;
        var remainingContainer = FindTabContentContainer(tabContentHost, remaining);
        var cachedElement = Assert.IsAssignableFrom<InputElement>(
            remainingContainer.GetVisualDescendants().OfType<Control>().First());
        KeyboardNavigation.SetTabOnceActiveElement(tabContentHost, cachedElement);

        root.Workspace.CloseTab(closed, rememberForRestore: false);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Same(cachedElement, KeyboardNavigation.GetTabOnceActiveElement(tabContentHost));
    }

    private static void OpenTabsMaterializePanelPeersAndCloseTarget(
        WorkspaceShellView view,
        ShellWorkspaceViewModel workspace)
    {
        var target = new TestTabPageViewModel { Name = "Closed" };
        var remaining = new TestTabPageViewModel { Name = "Remaining" };
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

    private static ContentPresenter FindTabContentContainer(
        ItemsControl tabContentHost,
        TabPageViewModelBase tab)
    {
        return Assert.Single(
            tabContentHost.GetVisualDescendants().OfType<ContentPresenter>(),
            presenter => ReferenceEquals(presenter.DataContext, tab) &&
                         presenter.GetVisualParent() is Panel);
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
            LayoutProfile: UiLayoutProfile.Workspace,
            Capabilities: new AppCapabilities(
                CanHostSyncServer: true,
                CanPairAsClient: true,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true,
                CanPickFolder: true,
                CanPlayVideo: false),
            Input: new InputCapabilities(
                HasPointer: true,
                HasTouch: false,
                HasKeyboard: true,
                SupportsLongPressContextMenu: false,
                CommandModifier: CommandModifier.Control));

        return ShellRootViewModelTestFactory.Create(
            MainPagesViewModelTestFactory.Create(),
            new ShellWorkspaceViewModel(TestDispatcher, timeProvider: new FakeTimeProvider()),
            environment,
            Substitute.For<IPlotZoomState>(),
            TestDispatcher,
            timeProvider: new FakeTimeProvider());
    }

    private static async Task<MountedWorkspaceShellView> MountAsync(WorkspaceShellView view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedWorkspaceShellView(host, view);
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
