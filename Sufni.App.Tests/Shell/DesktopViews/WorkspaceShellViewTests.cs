using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
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

    private static ShellRootViewModel CreateRoot()
    {
        var environment = new AppEnvironment(
            DefaultLayoutProfile: UiLayoutProfile.Workspace,
            LayoutProfile: UiLayoutProfile.Workspace,
            Capabilities: new AppCapabilities(
                CanHostSyncServer: true,
                CanPairAsClient: true,
                HasHaptics: false,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true,
                SupportsNativeWindowing: true),
            Input: new InputCapabilities(
                HasPointer: true,
                HasTouch: false,
                HasKeyboard: true,
                SupportsPinch: false,
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
