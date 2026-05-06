using DynamicData;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.SessionGraph;
using Sufni.App.ViewModels.ItemLists;

namespace Sufni.App.Tests.ViewModels.ItemLists;

public class SessionListViewModelTests
{
    [Fact]
    public async Task FinalizeDelete_KeepsSessionHidden_WhileDeleteInProgress()
    {
        var (graph, sessionCache) = CreateGraph();
        using (sessionCache)
        {
            var summary = CreateSummary(name: "to delete");
            sessionCache.AddOrUpdate(summary);

            var sessionCoordinator = TestCoordinatorSubstitutes.Session();
            var deleteTcs = new TaskCompletionSource<SessionDeleteResult>();
            sessionCoordinator.DeleteAsync(summary.Id).Returns(deleteTcs.Task);

            var viewModel = new SessionListViewModel(graph, sessionCoordinator);
            Assert.Single(viewModel.Items);

            viewModel.Items[0].UndoableDeleteCommand.Execute(null);
            Assert.Empty(viewModel.Items);

            var entry = viewModel.PendingDeletes[0];
            var finalizeTask = entry.FinalizeDeleteCommand.ExecuteAsync(null);
            Assert.Empty(viewModel.Items);

            sessionCache.Remove(summary.Id);
            deleteTcs.SetResult(new SessionDeleteResult(SessionDeleteOutcome.Deleted));
            await finalizeTask;

            Assert.Empty(viewModel.Items);
        }
    }

    [Fact]
    public async Task FinalizeDelete_RestoresSession_WhenCoordinatorReportsFailure()
    {
        var (graph, sessionCache) = CreateGraph();
        using (sessionCache)
        {
            var summary = CreateSummary(name: "stays");
            sessionCache.AddOrUpdate(summary);

            var sessionCoordinator = TestCoordinatorSubstitutes.Session();
            sessionCoordinator.DeleteAsync(summary.Id)
                .Returns(new SessionDeleteResult(SessionDeleteOutcome.Failed, "boom"));

            var viewModel = new SessionListViewModel(graph, sessionCoordinator);
            Assert.Single(viewModel.Items);

            viewModel.Items[0].UndoableDeleteCommand.Execute(null);
            var entry = viewModel.PendingDeletes[0];
            await entry.FinalizeDeleteCommand.ExecuteAsync(null);

            Assert.Single(viewModel.Items);
            Assert.Contains(viewModel.ErrorMessages, message => message.Contains("boom", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task RequestRowDelete_StacksMultipleEntries_WithoutForcingPriorFinalize()
    {
        var (graph, sessionCache) = CreateGraph();
        using (sessionCache)
        {
            var first = CreateSummary(name: "first");
            var second = CreateSummary(name: "second");
            sessionCache.AddOrUpdate(first);
            sessionCache.AddOrUpdate(second);

            var sessionCoordinator = TestCoordinatorSubstitutes.Session();
            var firstTcs = new TaskCompletionSource<SessionDeleteResult>();
            var secondTcs = new TaskCompletionSource<SessionDeleteResult>();
            sessionCoordinator.DeleteAsync(first.Id).Returns(firstTcs.Task);
            sessionCoordinator.DeleteAsync(second.Id).Returns(secondTcs.Task);

            var viewModel = new SessionListViewModel(graph, sessionCoordinator);
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

    [Fact]
    public void StaleRows_ShowSuffix_ButSearchUsesSummaryText()
    {
        var (graph, sessionCache) = CreateGraph();
        using (sessionCache)
        {
            var summary = CreateSummary(
                name: "Alpine",
                description: "rough track",
                staleness: new SessionStaleness.DependencyHashChanged());
            sessionCache.AddOrUpdate(summary);
            var viewModel = new SessionListViewModel(graph, TestCoordinatorSubstitutes.Session());

            var row = Assert.Single(viewModel.Items);
            Assert.True(row.IsStale);
            Assert.Equal("Alpine", row.BaseName);
            Assert.Equal("Alpine (Stale)", row.Name);

            viewModel.SearchText = "Stale";
            Assert.Empty(viewModel.Items);

            viewModel.SearchText = "rough";
            Assert.Single(viewModel.Items);
        }
    }

    [Fact]
    public void NoRawRows_ShowSuffix_ButAreNotStaleAndSearchUsesSummaryText()
    {
        var (graph, sessionCache) = CreateGraph();
        using (sessionCache)
        {
            var summary = CreateSummary(
                name: "Alpine",
                description: "rough track",
                staleness: new SessionStaleness.MissingRawSource());
            sessionCache.AddOrUpdate(summary);
            var viewModel = new SessionListViewModel(graph, TestCoordinatorSubstitutes.Session());

            var row = Assert.Single(viewModel.Items);
            Assert.False(row.IsStale);
            Assert.True(row.HasNoRawSource);
            Assert.Equal("Alpine", row.BaseName);
            Assert.Equal("Alpine (No Raw)", row.Name);

            viewModel.SearchText = "No Raw";
            Assert.Empty(viewModel.Items);

            viewModel.SearchText = "rough";
            Assert.Single(viewModel.Items);
        }
    }

    private static (IRecordedSessionGraph Graph, SourceCache<RecordedSessionSummary, Guid> Cache) CreateGraph()
    {
        var graph = Substitute.For<IRecordedSessionGraph>();
        var cache = new SourceCache<RecordedSessionSummary, Guid>(summary => summary.Id);
        graph.ConnectSessions().Returns(cache.Connect());
        return (graph, cache);
    }

    private static RecordedSessionSummary CreateSummary(
        string name,
        string description = "",
        SessionStaleness? staleness = null) => new(
        Guid.NewGuid(),
        name,
        description,
        Timestamp: null,
        HasProcessedData: true,
        staleness ?? new SessionStaleness.Current());
}
