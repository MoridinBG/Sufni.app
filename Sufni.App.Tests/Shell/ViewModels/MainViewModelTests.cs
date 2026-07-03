using NSubstitute;

using Sufni.App.Infrastructure;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Shell.Coordinators;
namespace Sufni.App.Tests.Shell.ViewModels;

public class MainViewModelTests
{
    [Fact]
    public void Constructor_SetsNavigationRootToMainPages()
    {
        var mainPages = MainPagesViewModelTestFactory.Create();
        var navigationHost = Substitute.For<IMobileNavigationShellHost>();
        var plotZoomState = Substitute.For<IPlotZoomState>();

        _ = new MainViewModel(mainPages, navigationHost, plotZoomState, new InlineUiThreadDispatcher());

        navigationHost.Received(1).SetRoot(mainPages);
    }

    [Fact]
    public void TryCloseTransientShellSurface_CollapsesZoomBeforeDrawer()
    {
        var plotZoomState = Substitute.For<IPlotZoomState>();
        plotZoomState.TryCollapse().Returns(true);
        var viewModel = CreateViewModel(plotZoomState);
        viewModel.MainPagesViewModel.IsDrawerOpen = true;

        var handled = viewModel.TryCloseTransientShellSurface();

        Assert.True(handled);
        Assert.True(viewModel.MainPagesViewModel.IsDrawerOpen);
        plotZoomState.Received(1).TryCollapse();
    }

    [Fact]
    public void TryCloseTransientShellSurface_ClosesDrawerAndReturnsTrue_WhenDrawerIsOpen()
    {
        var plotZoomState = Substitute.For<IPlotZoomState>();
        var viewModel = CreateViewModel(plotZoomState);
        viewModel.MainPagesViewModel.IsDrawerOpen = true;

        var handled = viewModel.TryCloseTransientShellSurface();

        Assert.True(handled);
        Assert.False(viewModel.MainPagesViewModel.IsDrawerOpen);
        plotZoomState.Received(1).TryCollapse();
    }

    [Fact]
    public void TryCloseTransientShellSurface_ReturnsFalse_WhenDrawerIsClosed()
    {
        var plotZoomState = Substitute.For<IPlotZoomState>();
        var viewModel = CreateViewModel(plotZoomState);

        var handled = viewModel.TryCloseTransientShellSurface();

        Assert.False(handled);
        Assert.False(viewModel.MainPagesViewModel.IsDrawerOpen);
        plotZoomState.Received(1).TryCollapse();
    }

    private static MainViewModel CreateViewModel(IPlotZoomState? plotZoomState = null) =>
        new(
            MainPagesViewModelTestFactory.Create(),
            Substitute.For<IMobileNavigationShellHost>(),
            plotZoomState ?? Substitute.For<IPlotZoomState>(),
            new InlineUiThreadDispatcher());
}
