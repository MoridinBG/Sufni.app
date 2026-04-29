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

        var entry = viewModel.PendingDeletes[0];
        var finalizeTask = entry.FinalizeDeleteCommand.ExecuteAsync(null);
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
        var entry = viewModel.PendingDeletes[0];
        await entry.FinalizeDeleteCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Items);
        Assert.Contains(viewModel.ErrorMessages, message => message.Contains("boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RequestRowDelete_StacksMultipleEntries_WithoutForcingPriorFinalize()
    {
        var sessionStore = Substitute.For<ISessionStore>();
        using var sessionCache = new SourceCache<SessionSnapshot, Guid>(snapshot => snapshot.Id);
        sessionStore.Connect().Returns(sessionCache.Connect());

        var first = TestSnapshots.Session(name: "first");
        var second = TestSnapshots.Session(name: "second");
        sessionCache.AddOrUpdate(first);
        sessionCache.AddOrUpdate(second);
        sessionStore.Get(first.Id).Returns(first);
        sessionStore.Get(second.Id).Returns(second);

        var sessionCoordinator = TestCoordinatorSubstitutes.Session();
        var firstTcs = new TaskCompletionSource<SessionDeleteResult>();
        var secondTcs = new TaskCompletionSource<SessionDeleteResult>();
        sessionCoordinator.DeleteAsync(first.Id).Returns(firstTcs.Task);
        sessionCoordinator.DeleteAsync(second.Id).Returns(secondTcs.Task);

        var viewModel = new SessionListViewModel(sessionStore, sessionCoordinator);
        Assert.Equal(2, viewModel.Items.Count);

        // Delete both rows back to back. The second delete must not
        // force the first one to finalize early.
        viewModel.Items[0].UndoableDeleteCommand.Execute(null);
        viewModel.Items[0].UndoableDeleteCommand.Execute(null);

        Assert.Empty(viewModel.Items);
        Assert.Equal(2, viewModel.PendingDeletes.Count);

        await sessionCoordinator.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
    }
}
