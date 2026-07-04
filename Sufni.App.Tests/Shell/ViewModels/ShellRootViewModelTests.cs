using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.ViewModels;

namespace Sufni.App.Tests.Shell.ViewModels;

public class ShellRootViewModelTests
{
    private static readonly InlineUiThreadDispatcher TestDispatcher = new();

    [Fact]
    public void Constructor_ExposesSharedShellStateAndEnvironment()
    {
        var pages = MainPagesViewModelTestFactory.Create();
        var workspace = new ShellWorkspaceViewModel(TestDispatcher);
        var environment = new AppEnvironment(
            DefaultLayoutProfile: UiLayoutProfile.Compact,
            LayoutProfile: UiLayoutProfile.Workspace,
            Capabilities: new AppCapabilities(
                CanHostSyncServer: true,
                CanPairAsClient: false,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true),
            Input: new InputCapabilities(
                HasPointer: true,
                HasTouch: false,
                HasKeyboard: true,
                SupportsLongPressContextMenu: false));

        var plotZoomState = Substitute.For<IPlotZoomState>();

        var root = new ShellRootViewModel(pages, workspace, environment, plotZoomState, TestDispatcher);

        Assert.Same(pages, root.Pages);
        Assert.Same(workspace, root.Workspace);
        Assert.Equal(UiLayoutProfile.Workspace, root.LayoutProfile);
        Assert.Same(environment.Capabilities, root.Capabilities);
    }

    [Fact]
    public void TryCloseTransientShellSurface_CollapsesPlotZoomFirst()
    {
        var plotZoomState = Substitute.For<IPlotZoomState>();
        plotZoomState.TryCollapse().Returns(true);
        var root = CreateRoot(plotZoomState);
        root.Pages.IsDrawerOpen = true;

        var handled = root.TryCloseTransientShellSurface();

        Assert.True(handled);
        Assert.True(root.Pages.IsDrawerOpen);
        plotZoomState.Received(1).TryCollapse();
    }

    [Fact]
    public void TryCloseTransientShellSurface_ClosesDrawerWhenZoomDoesNotHandle()
    {
        var plotZoomState = Substitute.For<IPlotZoomState>();
        var root = CreateRoot(plotZoomState);
        root.Pages.IsDrawerOpen = true;

        var handled = root.TryCloseTransientShellSurface();

        Assert.True(handled);
        Assert.False(root.Pages.IsDrawerOpen);
        plotZoomState.Received(1).TryCollapse();
    }

    [Fact]
    public void TryCloseTransientShellSurface_ReturnsFalseWhenNoTransientSurfaceIsOpen()
    {
        var plotZoomState = Substitute.For<IPlotZoomState>();
        var root = CreateRoot(plotZoomState);

        var handled = root.TryCloseTransientShellSurface();

        Assert.False(handled);
        Assert.False(root.Pages.IsDrawerOpen);
        plotZoomState.Received(1).TryCollapse();
    }

    [Fact]
    public void HandleBackRequest_ClosesTransientSurfaceBeforeWorkspaceBack()
    {
        var plotZoomState = Substitute.For<IPlotZoomState>();
        var root = CreateRoot(plotZoomState);
        var tab = new TestTabPage();
        root.Workspace.OpenOrFocus(tab);
        root.Pages.IsDrawerOpen = true;

        var handled = root.HandleBackRequest();

        Assert.True(handled);
        Assert.False(root.Pages.IsDrawerOpen);
        Assert.Same(tab, root.Workspace.CurrentTab);
    }

    [Fact]
    public void HandleBackRequest_DelegatesToWorkspaceGoBack()
    {
        var plotZoomState = Substitute.For<IPlotZoomState>();
        var root = CreateRoot(plotZoomState);
        var previous = new TestTabPage();
        var current = new TestTabPage();
        root.Workspace.OpenOrFocus(previous);
        root.Workspace.OpenOrFocus(current);

        var handled = root.HandleBackRequest();

        Assert.True(handled);
        Assert.Same(previous, root.Workspace.CurrentTab);
    }

    [Fact]
    public void HandleBackRequest_ReturnsFalseWhenWorkspaceCannotGoBack()
    {
        var plotZoomState = Substitute.For<IPlotZoomState>();
        var root = CreateRoot(plotZoomState);

        var handled = root.HandleBackRequest();

        Assert.False(handled);
    }

    private static ShellRootViewModel CreateRoot(IPlotZoomState plotZoomState)
    {
        var environment = new AppEnvironment(
            DefaultLayoutProfile: UiLayoutProfile.Compact,
            LayoutProfile: UiLayoutProfile.Compact,
            Capabilities: new AppCapabilities(
                CanHostSyncServer: false,
                CanPairAsClient: true,
                SupportsMassStorageImport: false,
                SupportsStorageProviderImport: true),
            Input: new InputCapabilities(
                HasPointer: false,
                HasTouch: true,
                HasKeyboard: false,
                SupportsLongPressContextMenu: true));

        return new ShellRootViewModel(
            MainPagesViewModelTestFactory.Create(),
            new ShellWorkspaceViewModel(TestDispatcher),
            environment,
            plotZoomState,
            TestDispatcher);
    }

    private sealed class TestTabPage : TabPageViewModelBase
    {
        public TestTabPage()
            : base(TestDispatcher)
        {
        }
    }
}
