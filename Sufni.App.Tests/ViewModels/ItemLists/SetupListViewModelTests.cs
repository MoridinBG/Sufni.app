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

    [Fact]
    public async Task FinalizeDelete_KeepsSetupHidden_WhileDeleteInProgress()
    {
        var setupStore = Substitute.For<ISetupStore>();
        using var setupCache = new SourceCache<SetupSnapshot, Guid>(snapshot => snapshot.Id);
        setupStore.Connect().Returns(setupCache.Connect());

        var snapshot = TestSnapshots.Setup(name: "to delete");
        setupCache.AddOrUpdate(snapshot);
        setupStore.Get(snapshot.Id).Returns(snapshot);

        var setupCoordinator = TestCoordinatorSubstitutes.Setup();
        var deleteTcs = new TaskCompletionSource<SetupDeleteResult>();
        setupCoordinator.DeleteAsync(snapshot.Id).Returns(deleteTcs.Task);

        var viewModel = new SetupListViewModel(setupStore, setupCoordinator);
        Assert.Single(viewModel.Items);

        viewModel.Items[0].UndoableDeleteCommand.Execute(null);
        Assert.Empty(viewModel.Items);

        var entry = viewModel.PendingDeletes[0];
        var finalizeTask = entry.FinalizeDeleteCommand.ExecuteAsync(null);
        Assert.Empty(viewModel.Items);

        setupCache.Remove(snapshot.Id);
        deleteTcs.SetResult(new SetupDeleteResult(SetupDeleteOutcome.Deleted));
        await finalizeTask;

        Assert.Empty(viewModel.Items);
    }

    [Fact]
    public async Task FinalizeDelete_RestoresSetup_WhenCoordinatorReportsFailure()
    {
        var setupStore = Substitute.For<ISetupStore>();
        using var setupCache = new SourceCache<SetupSnapshot, Guid>(snapshot => snapshot.Id);
        setupStore.Connect().Returns(setupCache.Connect());

        var snapshot = TestSnapshots.Setup(name: "stays");
        setupCache.AddOrUpdate(snapshot);
        setupStore.Get(snapshot.Id).Returns(snapshot);

        var setupCoordinator = TestCoordinatorSubstitutes.Setup();
        setupCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new SetupDeleteResult(SetupDeleteOutcome.Failed, "boom"));

        var viewModel = new SetupListViewModel(setupStore, setupCoordinator);
        Assert.Single(viewModel.Items);

        viewModel.Items[0].UndoableDeleteCommand.Execute(null);
        var entry = viewModel.PendingDeletes[0];
        await entry.FinalizeDeleteCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Items);
        Assert.Contains(viewModel.ErrorMessages, message => message.Contains("boom", StringComparison.Ordinal));
    }
}
