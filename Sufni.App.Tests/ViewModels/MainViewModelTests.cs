using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Tests.Views;
using Sufni.App.ViewModels;

namespace Sufni.App.Tests.ViewModels;

public class MainViewModelTests
{
    [Fact]
    public void Constructor_SetsNavigationRootToMainPages()
    {
        var mainPages = MainPagesViewModelTestFactory.Create();
        var navigationHost = Substitute.For<IMobileNavigationShellHost>();

        _ = new MainViewModel(mainPages, navigationHost, new InlineUiThreadDispatcher());

        navigationHost.Received(1).SetRoot(mainPages);
    }

    [Fact]
    public void TryCloseTransientShellSurface_ClosesDrawerAndReturnsTrue_WhenDrawerIsOpen()
    {
        var viewModel = CreateViewModel();
        viewModel.MainPagesViewModel.IsDrawerOpen = true;

        var handled = viewModel.TryCloseTransientShellSurface();

        Assert.True(handled);
        Assert.False(viewModel.MainPagesViewModel.IsDrawerOpen);
    }

    [Fact]
    public void TryCloseTransientShellSurface_ReturnsFalse_WhenDrawerIsClosed()
    {
        var viewModel = CreateViewModel();

        var handled = viewModel.TryCloseTransientShellSurface();

        Assert.False(handled);
        Assert.False(viewModel.MainPagesViewModel.IsDrawerOpen);
    }

    private static MainViewModel CreateViewModel() =>
        new(
            MainPagesViewModelTestFactory.Create(),
            Substitute.For<IMobileNavigationShellHost>(),
            new InlineUiThreadDispatcher());
}
