using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Tests.Views;
using Sufni.App.Stores;
using Sufni.App.ViewModels.ItemLists;

namespace Sufni.App.Tests.ViewModels;

public class MainPagesViewModelTests
{
    [Fact]
    public void SelectedIndex_ActivatesLivePage_WhenSelected_AndDeactivatesIt_WhenLeft()
    {
        var liveCoordinator = TestCoordinatorSubstitutes.LiveDaq();
        var livePage = new LiveDaqListViewModel(new LiveDaqStore(), liveCoordinator);
        var viewModel = MainPagesViewModelTestFactory.Create(livePage);

        viewModel.SelectedIndex = 3;
        viewModel.SelectedIndex = 0;

        liveCoordinator.Received(1).Activate();
        liveCoordinator.Received(1).Deactivate();
    }

    [Fact]
    public void LiveDaqsPage_IsAlwaysProvided()
    {
        var livePage = new LiveDaqListViewModel(new LiveDaqStore(), TestCoordinatorSubstitutes.LiveDaq());
        var viewModel = MainPagesViewModelTestFactory.Create(livePage);

        Assert.Same(livePage, viewModel.LiveDaqsPage);
    }
}