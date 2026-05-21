using System.Globalization;
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
    public async Task RecalculateRow_CallsCoordinatorWithSummaryUpdatedBaseline()
    {
        var (graph, sessionCache) = CreateGraph();
        using (sessionCache)
        {
            var summary = CreateSummary(
                name: "stale",
                updated: 42,
                staleness: new SessionStaleness.DependencyHashChanged());
            sessionCache.AddOrUpdate(summary);

            var sessionCoordinator = TestCoordinatorSubstitutes.Session();
            sessionCoordinator.RecomputeAsync(summary.Id, summary.Updated, Arg.Any<CancellationToken>())
                .Returns(new SessionRecomputeResult.Recomputed(summary.Updated + 1));

            var viewModel = new SessionListViewModel(graph, sessionCoordinator);
            var row = Assert.Single(viewModel.Items);

            Assert.True(row.RecalculateCommand.CanExecute(null));
            await row.RecalculateCommand.ExecuteAsync(null);

            await sessionCoordinator.Received(1)
                .RecomputeAsync(summary.Id, summary.Updated, Arg.Any<CancellationToken>());
            Assert.Empty(viewModel.ErrorMessages);
        }
    }

    [Fact]
    public async Task RecalculateRow_ReportsCoordinatorFailure()
    {
        var (graph, sessionCache) = CreateGraph();
        using (sessionCache)
        {
            var summary = CreateSummary(
                name: "stale",
                updated: 42,
                staleness: new SessionStaleness.DependencyHashChanged());
            sessionCache.AddOrUpdate(summary);

            var sessionCoordinator = TestCoordinatorSubstitutes.Session();
            sessionCoordinator.RecomputeAsync(summary.Id, summary.Updated, Arg.Any<CancellationToken>())
                .Returns(new SessionRecomputeResult.Failed("boom"));

            var viewModel = new SessionListViewModel(graph, sessionCoordinator);
            await viewModel.Items[0].RecalculateCommand.ExecuteAsync(null);

            Assert.Contains(viewModel.ErrorMessages, message => message.Contains("boom", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task RecalculateRow_CallsCoordinator_WhenSummaryIsCurrent()
    {
        var (graph, sessionCache) = CreateGraph();
        using (sessionCache)
        {
            var summary = CreateSummary(
                name: "current",
                updated: 42,
                staleness: new SessionStaleness.Current());
            sessionCache.AddOrUpdate(summary);

            var sessionCoordinator = TestCoordinatorSubstitutes.Session();
            sessionCoordinator.RecomputeAsync(summary.Id, summary.Updated, Arg.Any<CancellationToken>())
                .Returns(new SessionRecomputeResult.Recomputed(summary.Updated + 1));

            var viewModel = new SessionListViewModel(graph, sessionCoordinator);
            var row = Assert.Single(viewModel.Items);

            Assert.True(row.RecalculateCommand.CanExecute(null));
            await row.RecalculateCommand.ExecuteAsync(null);

            await sessionCoordinator.Received(1)
                .RecomputeAsync(summary.Id, summary.Updated, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public void RecalculateRow_Disabled_WhenSummaryCannotManuallyRecompute()
    {
        var (graph, sessionCache) = CreateGraph();
        using (sessionCache)
        {
            sessionCache.AddOrUpdate(CreateSummary(
                name: "no raw",
                staleness: new SessionStaleness.MissingRawSource()));

            var viewModel = new SessionListViewModel(graph, TestCoordinatorSubstitutes.Session());
            var row = Assert.Single(viewModel.Items);

            Assert.False(row.RecalculateCommand.CanExecute(null));
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
            Assert.Equal("Alpine", row.Name);
            Assert.Equal("Alpine (Stale)", row.TitleText);

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
            Assert.Equal("Alpine", row.Name);
            Assert.Equal("Alpine (No Raw)", row.TitleText);

            viewModel.SearchText = "No Raw";
            Assert.Empty(viewModel.Items);

            viewModel.SearchText = "rough";
            Assert.Single(viewModel.Items);
        }
    }

    [Fact]
    public void DateGroups_GroupRowsByLocalDateDescending_WithNoDateLast()
    {
        var (graph, sessionCache) = CreateGraph();
        using (sessionCache)
        {
            var newest = CreateSummary("newest", timestamp: ToUnixSeconds(2026, 5, 20, 9, 15));
            var sameDay = CreateSummary("same day", timestamp: ToUnixSeconds(2026, 5, 20, 16, 30));
            var older = CreateSummary("older", timestamp: ToUnixSeconds(2026, 5, 19, 8, 0));
            var undated = CreateSummary("undated");
            sessionCache.AddOrUpdate(newest);
            sessionCache.AddOrUpdate(sameDay);
            sessionCache.AddOrUpdate(older);
            sessionCache.AddOrUpdate(undated);

            var viewModel = new SessionListViewModel(graph, TestCoordinatorSubstitutes.Session());

            Assert.Equal(3, viewModel.DateGroups.Count);
            Assert.Equal(new DateOnly(2026, 5, 20), viewModel.DateGroups[0].Key.Date);
            Assert.Equal(new DateOnly(2026, 5, 19), viewModel.DateGroups[1].Key.Date);
            Assert.Equal(SessionDateGroupKey.NoDate, viewModel.DateGroups[2].Key);
            Assert.Equal(2, viewModel.DateGroups[0].Items.Count);
            Assert.Single(viewModel.DateGroups[2].Items);
            Assert.All(viewModel.DateGroups, group => Assert.True(group.IsExpanded));
        }
    }

    [Fact]
    public void DateGroups_PreserveExistingGroupAndRowsCollection_WhenRowsChange()
    {
        var (graph, sessionCache) = CreateGraph();
        using (sessionCache)
        {
            sessionCache.AddOrUpdate(CreateSummary("Morning", timestamp: ToUnixSeconds(2026, 5, 20, 9, 15)));

            var viewModel = new SessionListViewModel(graph, TestCoordinatorSubstitutes.Session());
            var group = Assert.Single(viewModel.DateGroups);
            var rows = group.Items;

            sessionCache.AddOrUpdate(CreateSummary("Afternoon", timestamp: ToUnixSeconds(2026, 5, 20, 16, 30)));

            var updatedGroup = Assert.Single(viewModel.DateGroups);
            Assert.Same(group, updatedGroup);
            Assert.Same(rows, updatedGroup.Items);
            Assert.Equal(2, updatedGroup.Items.Count);
        }
    }

    [Fact]
    public void DateGroups_PreserveCollapsedState_WhenFilteringRemovesAndRestoresGroup()
    {
        var (graph, sessionCache) = CreateGraph();
        using (sessionCache)
        {
            sessionCache.AddOrUpdate(CreateSummary("Alpine", timestamp: ToUnixSeconds(2026, 5, 20, 9, 15)));
            sessionCache.AddOrUpdate(CreateSummary("Valley", timestamp: ToUnixSeconds(2026, 5, 19, 8, 0)));

            var viewModel = new SessionListViewModel(graph, TestCoordinatorSubstitutes.Session());
            viewModel.DateGroups[0].ToggleExpandedCommand.Execute(null);

            Assert.False(viewModel.DateGroups[0].IsExpanded);

            viewModel.SearchText = "Valley";
            Assert.Single(viewModel.DateGroups);
            Assert.Equal(new DateOnly(2026, 5, 19), viewModel.DateGroups[0].Key.Date);

            viewModel.SearchText = null;
            Assert.Equal(2, viewModel.DateGroups.Count);
            Assert.Equal(new DateOnly(2026, 5, 20), viewModel.DateGroups[0].Key.Date);
            Assert.False(viewModel.DateGroups[0].IsExpanded);
        }
    }

    [Fact]
    public void RequestRowDelete_RemovesEmptyDateGroupDuringUndoWindow()
    {
        var (graph, sessionCache) = CreateGraph();
        using (sessionCache)
        {
            var summary = CreateSummary("delete", timestamp: ToUnixSeconds(2026, 5, 20, 9, 15));
            sessionCache.AddOrUpdate(summary);

            var viewModel = new SessionListViewModel(graph, TestCoordinatorSubstitutes.Session());
            Assert.Single(viewModel.DateGroups);

            viewModel.Items[0].UndoableDeleteCommand.Execute(null);

            Assert.Empty(viewModel.Items);
            Assert.Empty(viewModel.DateGroups);

            viewModel.PendingDeletes[0].UndoDeleteCommand.Execute(null);
            Assert.Single(viewModel.DateGroups);
        }
    }

    [Fact]
    public void RowPresentation_FormatsTitleTimestampAndGroupKey()
    {
        var (graph, sessionCache) = CreateGraph();
        using (sessionCache)
        {
            var timestamp = ToUnixSeconds(2026, 5, 20, 9, 15);
            var summary = CreateSummary(
                name: "Alpine",
                timestamp: timestamp,
                staleness: new SessionStaleness.DependencyHashChanged());
            sessionCache.AddOrUpdate(summary);

            var viewModel = new SessionListViewModel(graph, TestCoordinatorSubstitutes.Session());
            var row = Assert.Single(viewModel.Items);
            var expectedTimestamp = row.Timestamp!.Value.ToString(
                CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern,
                CultureInfo.CurrentCulture);

            Assert.Equal("Alpine (Stale)", row.TitleText);
            Assert.Equal(expectedTimestamp, row.TimestampText);
            Assert.Equal(new DateOnly(2026, 5, 20), row.DateGroupKey.Date);
            Assert.Empty(row.SubtitleText);
        }
    }

    [Fact]
    public void RowPresentation_FormatsSubtitleFromSummaryMetrics()
    {
        var (graph, sessionCache) = CreateGraph();
        using (sessionCache)
        {
            var summary = CreateSummary(
                name: "Metrics",
                durationSeconds: 65,
                distanceMeters: 987,
                ascentMeters: 12.4,
                descentMeters: 4.2);
            sessionCache.AddOrUpdate(summary);

            var viewModel = new SessionListViewModel(graph, TestCoordinatorSubstitutes.Session());
            var row = Assert.Single(viewModel.Items);

            Assert.Equal("1m 05s | 987 m | +12 m / -4 m", row.SubtitleText);
            Assert.True(row.HasSubtitleText);
        }
    }

    [Fact]
    public void RowPresentation_OmitsGpsSubtitle_WhenOnlyDurationIsKnown()
    {
        var (graph, sessionCache) = CreateGraph();
        using (sessionCache)
        {
            sessionCache.AddOrUpdate(CreateSummary(name: "Duration", durationSeconds: 3725));

            var viewModel = new SessionListViewModel(graph, TestCoordinatorSubstitutes.Session());
            var row = Assert.Single(viewModel.Items);

            Assert.Equal("1h 02m", row.SubtitleText);
            Assert.True(row.HasSubtitleText);
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
        long updated = 1,
        long? timestamp = null,
        SessionStaleness? staleness = null,
        double? durationSeconds = null,
        double? distanceMeters = null,
        double? ascentMeters = null,
        double? descentMeters = null) => new(
        Guid.NewGuid(),
        updated,
        name,
        description,
        Timestamp: timestamp,
        HasProcessedData: true,
        staleness ?? new SessionStaleness.Current(),
        DurationSeconds: durationSeconds,
        DistanceMeters: distanceMeters,
        AscentMeters: ascentMeters,
        DescentMeters: descentMeters);

    private static long ToUnixSeconds(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local);
        return new DateTimeOffset(local).ToUnixTimeSeconds();
    }
}
