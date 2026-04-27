using DynamicData;
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
        using var setupCache = new SourceCache<SetupSnapshot, Guid>(snapshot => snapshot.Id);
        var setupCoordinator = TestCoordinatorSubstitutes.Setup();

        setupStore.Connect().Returns(setupCache.Connect());

        var viewModel = new SetupListViewModel(setupStore, setupCoordinator);

        viewModel.AddCommand.Execute(null);

        _ = setupCoordinator.Received(1).OpenCreateAsync(null);
    }
}
