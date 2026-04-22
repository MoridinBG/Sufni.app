using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Stores;
using Sufni.App.ViewModels.ItemLists;

namespace Sufni.App.Tests.ViewModels.ItemLists;

public class SetupListViewModelTests
{
    [Fact]
    public void AddCommand_OpensSetupCreate_WithoutImportPageBoardContext()
    {
        var setupStore = Substitute.For<ISetupStore>();
        var setupCoordinator = Substitute.For<ISetupCoordinator>();
        var viewModel = new SetupListViewModel(setupStore, setupCoordinator);

        viewModel.AddCommand.Execute(null);

        _ = setupCoordinator.Received(1).OpenCreateAsync(null);
    }
}