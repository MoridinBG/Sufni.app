using DynamicData;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Stores;
using Sufni.App.ViewModels.ItemLists;

namespace Sufni.App.Tests.ViewModels.ItemLists;

public class SessionListViewModelTests
{
    [Fact]
    public async Task FinalizeDelete_KeepsSessionHidden_WhileDeleteInProgress()
    {
        var sessionStore = Substitute.For<ISessionStore>();
        using var sessionCache = new SourceCache<SessionSnapshot, Guid>(snapshot => snapshot.Id);
        sessionStore.Connect().Returns(sessionCache.Connect());

        var snapshot = TestSnapshots.Session(name: "to delete");
        sessionCache.AddOrUpdate(snapshot);
        sessionStore.Get(snapshot.Id).Returns(snapshot);

        var sessionCoordinator = TestCoordinatorSubstitutes.Session();
        var deleteTcs = new TaskCompletionSource<SessionDeleteResult>();
        sessionCoordinator.DeleteAsync(snapshot.Id).Returns(deleteTcs.Task);

        var viewModel = new SessionListViewModel(sessionStore, sessionCoordinator);
        Assert.Single(viewModel.Items);

        viewModel.Items[0].UndoableDeleteCommand.Execute(null);
        Assert.Empty(viewModel.Items);

        var finalizeTask = viewModel.FinalizeDeleteCommand.ExecuteAsync(null);
        Assert.Empty(viewModel.Items);

        sessionCache.Remove(snapshot.Id);
        deleteTcs.SetResult(new SessionDeleteResult(SessionDeleteOutcome.Deleted));
        await finalizeTask;

        Assert.Empty(viewModel.Items);
    }

    [Fact]
    public async Task FinalizeDelete_RestoresSession_WhenCoordinatorReportsFailure()
    {
        var sessionStore = Substitute.For<ISessionStore>();
        using var sessionCache = new SourceCache<SessionSnapshot, Guid>(snapshot => snapshot.Id);
        sessionStore.Connect().Returns(sessionCache.Connect());

        var snapshot = TestSnapshots.Session(name: "stays");
        sessionCache.AddOrUpdate(snapshot);
        sessionStore.Get(snapshot.Id).Returns(snapshot);

        var sessionCoordinator = TestCoordinatorSubstitutes.Session();
        sessionCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new SessionDeleteResult(SessionDeleteOutcome.Failed, "boom"));

        var viewModel = new SessionListViewModel(sessionStore, sessionCoordinator);
        Assert.Single(viewModel.Items);

        viewModel.Items[0].UndoableDeleteCommand.Execute(null);
        await viewModel.FinalizeDeleteCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Items);
        Assert.Contains(viewModel.ErrorMessages, message => message.Contains("boom", StringComparison.Ordinal));
    }
}
