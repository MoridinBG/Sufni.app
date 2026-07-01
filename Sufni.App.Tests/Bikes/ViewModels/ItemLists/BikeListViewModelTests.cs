using DynamicData;
using NSubstitute;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.ItemLists;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.ViewModels.ItemLists;

public class BikeListViewModelTests
{
    private static readonly InlineUiThreadDispatcher UiThreadDispatcher = new();

    [Fact]
    public async Task FinalizeDelete_KeepsBikeHidden_WhileDeleteInProgress()
    {
        var bikeStore = Substitute.For<IBikeStore>();
        using var bikeCache = new SourceCache<BikeSnapshot, Guid>(snapshot => snapshot.Id);
        bikeStore.Connect().Returns(bikeCache.Connect());

        var snapshot = TestSnapshots.Bike(name: "to delete");
        bikeCache.AddOrUpdate(snapshot);
        bikeStore.Get(snapshot.Id).Returns(snapshot);

        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        var deleteTcs = new TaskCompletionSource<BikeDeleteResult>();
        bikeCoordinator.DeleteAsync(snapshot.Id).Returns(deleteTcs.Task);

        var dependencyQuery = Substitute.For<IBikeDependencyQuery>();

        var viewModel = new BikeListViewModel(bikeStore, bikeCoordinator, dependencyQuery, UiThreadDispatcher);
        Assert.Single(viewModel.Items);

        viewModel.Items[0].UndoableDeleteCommand.Execute(null);
        Assert.Empty(viewModel.Items);

        var entry = viewModel.PendingDeletes[0];
        var finalizeTask = entry.FinalizeDeleteCommand.ExecuteAsync(null);
        Assert.Empty(viewModel.Items);

        bikeCache.Remove(snapshot.Id);
        deleteTcs.SetResult(new BikeDeleteResult(BikeDeleteOutcome.Deleted));
        await finalizeTask;

        Assert.Empty(viewModel.Items);
    }

    [Fact]
    public async Task FinalizeDelete_RestoresBike_WhenCoordinatorReportsFailure()
    {
        var bikeStore = Substitute.For<IBikeStore>();
        using var bikeCache = new SourceCache<BikeSnapshot, Guid>(snapshot => snapshot.Id);
        bikeStore.Connect().Returns(bikeCache.Connect());

        var snapshot = TestSnapshots.Bike(name: "stays");
        bikeCache.AddOrUpdate(snapshot);
        bikeStore.Get(snapshot.Id).Returns(snapshot);

        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        bikeCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new BikeDeleteResult(BikeDeleteOutcome.Failed, "boom"));

        var dependencyQuery = Substitute.For<IBikeDependencyQuery>();

        var viewModel = new BikeListViewModel(bikeStore, bikeCoordinator, dependencyQuery, UiThreadDispatcher);
        Assert.Single(viewModel.Items);

        viewModel.Items[0].UndoableDeleteCommand.Execute(null);
        var entry = viewModel.PendingDeletes[0];
        await entry.FinalizeDeleteCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Items);
        Assert.Contains(viewModel.ErrorMessages, message => message.Contains("boom", StringComparison.Ordinal));
    }
}
