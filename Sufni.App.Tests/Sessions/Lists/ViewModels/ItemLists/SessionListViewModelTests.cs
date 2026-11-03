using Sufni.App.Shared.Models;
using Sufni.App.Shell.Coordinators;
using Microsoft.Extensions.Time.Testing;
using System.Globalization;
using DynamicData;
using NSubstitute;
using Sufni.App.Infrastructure;

using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Lists.ViewModels.ItemLists;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Video;
using Sufni.App.Video.Stores;
namespace Sufni.App.Tests.Sessions.Lists.ViewModels.ItemLists;

public class SessionListViewModelTests
{
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private static readonly InlineUiThreadDispatcher UiThreadDispatcher = new();

    [Fact]
    public async Task FinalizeDelete_KeepsSessionHidden_WhileDeleteInProgress()
    {
        var (projection, sessionCache) = CreateProjection();
        using (sessionCache)
        {
            var summary = CreateSummary(name: "to delete");
            sessionCache.AddOrUpdate(summary);

            var sessionCoordinator = TestCoordinatorSubstitutes.Session();
            var deleteTcs = new TaskCompletionSource<SessionDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            sessionCoordinator.DeleteAsync(summary.Id).Returns(deleteTcs.Task);

            var viewModel = CreateLoadedViewModel(projection, sessionCoordinator, UiThreadDispatcher);
            Assert.Single(viewModel.Items);

            viewModel.Items[0].UndoableDeleteCommand.Execute(null);
            Assert.Empty(viewModel.Items);

            var entry = viewModel.PendingDeletes[0];
            var finalizeTask = entry.FinalizeDeleteCommand.ExecuteAsync(null);
            Assert.Empty(viewModel.Items);
            Assert.False(finalizeTask.IsCompleted);

            sessionCache.Remove(summary.Id);
            deleteTcs.SetResult(new SessionDeleteResult.Deleted());
            await finalizeTask.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(viewModel.PendingDeletes);

            Assert.Empty(viewModel.Items);
        }
    }

    [Fact]
    public async Task FinalizeDelete_RestoresSession_WhenCoordinatorReportsFailure()
    {
        var (projection, sessionCache) = CreateProjection();
        using (sessionCache)
        {
            var summary = CreateSummary(name: "stays");
            sessionCache.AddOrUpdate(summary);

            var sessionCoordinator = TestCoordinatorSubstitutes.Session();
            sessionCoordinator.DeleteAsync(summary.Id)
                .Returns(new SessionDeleteResult.Failed(SessionDeleteFailureReason.Delete, "boom"));

            var viewModel = CreateLoadedViewModel(projection, sessionCoordinator, UiThreadDispatcher);
            Assert.Single(viewModel.Items);

            viewModel.Items[0].UndoableDeleteCommand.Execute(null);
            var entry = viewModel.PendingDeletes[0];
            await entry.FinalizeDeleteCommand.ExecuteAsync(null);

            Assert.Single(viewModel.Items);
            Assert.NotEmpty(viewModel.ErrorMessages);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecalculateRow_DisablesDuplicateExecutionUntilRequestCompletes(bool stale)
    {
        var (projection, sessionCache) = CreateProjection();
        using (sessionCache)
        {
            var summary = CreateSummary(
                name: "stale",
                updated: 42,
                staleness: stale ? new SessionStaleness.Stale() : new SessionStaleness.Current());
            sessionCache.AddOrUpdate(summary);

            var recomputeCoordinator = TestCoordinatorSubstitutes.Session();
            var completion = new TaskCompletionSource<SessionRecomputeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            recomputeCoordinator.RecomputeAsync(summary.Id).Returns(completion.Task);

            var viewModel = CreateLoadedViewModel(projection, TestCoordinatorSubstitutes.Session(), UiThreadDispatcher, recomputeCoordinator: recomputeCoordinator);
            var row = Assert.Single(viewModel.Items);

            Assert.True(row.RecalculateCommand.CanExecute(null));
            var request = row.RecalculateCommand.ExecuteAsync(null);
            Assert.True(row.RecalculateCommand.IsRunning);
            Assert.False(row.RecalculateCommand.CanExecute(null));
            completion.SetResult(new SessionRecomputeResult.Recomputed());
            await request.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
            Assert.False(row.RecalculateCommand.IsRunning);
            Assert.True(row.RecalculateCommand.CanExecute(null));
            Assert.Empty(viewModel.ErrorMessages);
        }
    }

    [Fact]
    public void Rows_ShowProjectedMissingVideo()
    {
        var (projection, sessionCache) = CreateProjection();
        using (sessionCache)
        using (var videos = new VideoFixture())
        {
            var summary = CreateSummary(name: "session");
            sessionCache.AddOrUpdate(summary);
            sessionCache.AddOrUpdate(summary with { LatestVideo =
                videos.CreateClip("source-hash", startTimestamp: 0, durationSeconds: 60, fileExists: false) });
            var viewModel = CreateLoadedViewModel(projection, TestCoordinatorSubstitutes.Session(), UiThreadDispatcher);

            var row = Assert.Single(viewModel.Items);
            Assert.True(row.HasMissingVideo);
            Assert.False(row.HasPresentVideo);
        }
    }

    [Fact]
    public void Rows_ShowPresentVideo_WhenProjectionAddsClipAfterLoad()
    {
        var (projection, sessionCache) = CreateProjection();
        using (sessionCache)
        using (var videos = new VideoFixture())
        {
            var summary = CreateSummary(name: "session");
            sessionCache.AddOrUpdate(summary);
            var viewModel = CreateLoadedViewModel(projection, TestCoordinatorSubstitutes.Session(), UiThreadDispatcher);
            var row = Assert.Single(viewModel.Items);

            sessionCache.AddOrUpdate(summary with { LatestVideo =
                videos.CreateClip("source-hash", startTimestamp: 0, durationSeconds: 60) });

            var updated = Assert.Single(viewModel.Items);
            Assert.Equal(summary.Id, updated.Id);
            Assert.True(updated.HasPresentVideo);
            Assert.False(updated.HasMissingVideo);
        }
    }

    [Fact]
    public void ItemsAndDateGroups_UpdateOnlyWhileLoaded_AndReloadCurrentState()
    {
        var (projection, sessionCache) = CreateProjection();
        using (sessionCache)
        {
            sessionCache.AddOrUpdate(CreateSummary(
                "newest",
                timestamp: ToUnixSeconds(2026, 5, 20, 9, 15)));
            var viewModel = new SessionListViewModel(
                projection,
                TestCoordinatorSubstitutes.Session(),
                    UiThreadDispatcher,
                pendingDeletes: TestPendingDeletes.Create(sessions: TestCoordinatorSubstitutes.Session(), shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());

            Assert.Empty(viewModel.Items);
            Assert.Empty(viewModel.DateGroups);

            viewModel.LoadedCommand.Execute(null);
            Assert.Equal("newest", Assert.Single(viewModel.Items).BaseName);

            viewModel.UnloadedCommand.Execute(null);
            sessionCache.AddOrUpdate(CreateSummary(
                "older",
                timestamp: ToUnixSeconds(2026, 5, 19, 9, 15)));
            Assert.Empty(viewModel.Items);
            Assert.Empty(viewModel.DateGroups);

            viewModel.LoadedCommand.Execute(null);
            viewModel.LoadedCommand.Execute(null);
            Assert.Equal(2, viewModel.Items.Count);
            Assert.Equal(2, viewModel.DateGroups.Count);
            Assert.Equal(new DateOnly(2026, 5, 20), viewModel.DateGroups[0].Key.Date);
            Assert.Equal(new[] { "newest", "older" }, viewModel.Items.Select(row => row.BaseName));
            viewModel.UnloadedCommand.Execute(null);
        }
    }

    [Fact]
    public async Task RecalculateRow_ReportsCoordinatorFailure()
    {
        var (projection, sessionCache) = CreateProjection();
        using (sessionCache)
        {
            var summary = CreateSummary(
                name: "stale",
                updated: 42,
                staleness: new SessionStaleness.Stale());
            sessionCache.AddOrUpdate(summary);

            var recomputeCoordinator = TestCoordinatorSubstitutes.Session();
            recomputeCoordinator.RecomputeAsync(summary.Id)
                .Returns(new SessionRecomputeResult.Failed(SessionRecomputeFailureReason.Recompute, "boom"));

            var viewModel = CreateLoadedViewModel(projection, TestCoordinatorSubstitutes.Session(), UiThreadDispatcher, recomputeCoordinator: recomputeCoordinator);
            await viewModel.Items[0].RecalculateCommand.ExecuteAsync(null);

            Assert.Equal(FailurePresentation.From(SessionRecomputeFailureReason.Recompute, "boom"), Assert.Single(viewModel.ErrorMessages));
            Assert.Equal(summary.Id, Assert.Single(viewModel.Items).Id);
        }
    }

    [Fact]
    public void RecalculateRow_Disabled_WhenSummaryCannotManuallyRecompute()
    {
        var (projection, sessionCache) = CreateProjection();
        using (sessionCache)
        {
            sessionCache.AddOrUpdate(CreateSummary(
                name: "no raw",
                staleness: new SessionStaleness.MissingRawSource()));

            var viewModel = CreateLoadedViewModel(projection, TestCoordinatorSubstitutes.Session(), UiThreadDispatcher);
            var row = Assert.Single(viewModel.Items);

            Assert.False(row.RecalculateCommand.CanExecute(null));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Search_UsesAuthoredNameAndDescriptionWithoutStatusSuffix(bool missingSource)
    {
        var (projection, sessionCache) = CreateProjection();
        using (sessionCache)
        {
            var summary = CreateSummary(
                name: "Alpine",
                description: "rough track",
                staleness: missingSource ? new SessionStaleness.MissingRawSource() : new SessionStaleness.Stale());
            sessionCache.AddOrUpdate(summary);
            var viewModel = CreateLoadedViewModel(projection, TestCoordinatorSubstitutes.Session(), UiThreadDispatcher);

            var row = Assert.Single(viewModel.Items);
            viewModel.SearchText = row.TitleText.Replace(row.BaseName, "", StringComparison.Ordinal).Trim();
            Assert.Empty(viewModel.Items);

            viewModel.SearchText = "rough";
            Assert.Equal(summary.Id, Assert.Single(viewModel.Items).Id);
            viewModel.SearchText = "Alpine";
            Assert.Equal(summary.Id, Assert.Single(viewModel.Items).Id);
        }
    }

    [Fact]
    public void DateGroups_GroupRowsByLocalDateDescending_WithNoDateLast()
    {
        var (projection, sessionCache) = CreateProjection();
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

            var viewModel = CreateLoadedViewModel(projection, TestCoordinatorSubstitutes.Session(), UiThreadDispatcher);

            Assert.Equal(3, viewModel.DateGroups.Count);
            Assert.Equal(new DateOnly(2026, 5, 20), viewModel.DateGroups[0].Key.Date);
            Assert.Equal(new DateOnly(2026, 5, 19), viewModel.DateGroups[1].Key.Date);
            Assert.Equal(SessionDateGroupKey.NoDate, viewModel.DateGroups[2].Key);
            Assert.Equal(new[] { sameDay.Id, newest.Id }, viewModel.DateGroups[0].Items.Select(row => row.Id));
            Assert.Equal(older.Id, Assert.Single(viewModel.DateGroups[1].Items).Id);
            Assert.Equal(undated.Id, Assert.Single(viewModel.DateGroups[2].Items).Id);
        }
    }

    [Fact]
    public void DateFilterTo_IncludesEntireSelectedDay()
    {
        var (projection, sessionCache) = CreateProjection();
        using (sessionCache)
        {
            sessionCache.AddOrUpdate(CreateSummary("morning", timestamp: ToUnixSeconds(2026, 5, 20, 9, 15)));
            sessionCache.AddOrUpdate(CreateSummary("evening", timestamp: ToUnixSeconds(2026, 5, 20, 23, 59)));
            sessionCache.AddOrUpdate(CreateSummary("next day", timestamp: ToUnixSeconds(2026, 5, 21, 0, 0)));

            var viewModel = CreateLoadedViewModel(
                projection,
                TestCoordinatorSubstitutes.Session(),
                UiThreadDispatcher);
            viewModel.DateFilterTo = new DateTime(2026, 5, 20);

            Assert.Equal(new[] { "evening", "morning" }, viewModel.Items.Select(row => row.BaseName));
        }
    }

    [Fact]
    public async Task QueuedDateGroupSync_DoesNotRepublishAfterUnload()
    {
        var (projection, sessionCache) = CreateProjection();
        using (sessionCache)
        {
            var dispatcher = new ManualUiThreadDispatcher();
            sessionCache.AddOrUpdate(CreateSummary("Morning", timestamp: ToUnixSeconds(2026, 5, 20, 9, 15)));
            var viewModel = CreateLoadedViewModel(
                projection,
                TestCoordinatorSubstitutes.Session(),
                dispatcher);

            sessionCache.AddOrUpdate(CreateSummary("Afternoon", timestamp: ToUnixSeconds(2026, 5, 20, 16, 30)));

            viewModel.UnloadedCommand.Execute(null);
            await dispatcher.DrainAsync();

            Assert.Empty(viewModel.Items);
            Assert.Empty(viewModel.DateGroups);
        }
    }

    [Fact]
    public void DateGroups_PreserveCollapsedState_WhenFilteringRemovesAndRestoresGroup()
    {
        var (projection, sessionCache) = CreateProjection();
        using (sessionCache)
        {
            sessionCache.AddOrUpdate(CreateSummary("Alpine", timestamp: ToUnixSeconds(2026, 5, 20, 9, 15)));
            sessionCache.AddOrUpdate(CreateSummary("Valley", timestamp: ToUnixSeconds(2026, 5, 19, 8, 0)));

            var viewModel = CreateLoadedViewModel(projection, TestCoordinatorSubstitutes.Session(), UiThreadDispatcher);
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
        var (projection, sessionCache) = CreateProjection();
        using (sessionCache)
        {
            var summary = CreateSummary("delete", timestamp: ToUnixSeconds(2026, 5, 20, 9, 15));
            sessionCache.AddOrUpdate(summary);

            var viewModel = CreateLoadedViewModel(projection, TestCoordinatorSubstitutes.Session(), UiThreadDispatcher);
            Assert.Single(viewModel.DateGroups);

            viewModel.Items[0].UndoableDeleteCommand.Execute(null);

            Assert.Empty(viewModel.Items);
            Assert.Empty(viewModel.DateGroups);

            viewModel.PendingDeletes[0].UndoDeleteCommand.Execute(null);
            Assert.Single(viewModel.DateGroups);
        }
    }

    private SessionListViewModel CreateLoadedViewModel(
        IRecordedSessionProjection projection,
        ISessionCoordinator sessionCoordinator,
        IUiThreadDispatcher uiThreadDispatcher,
        ISessionCoordinator? recomputeCoordinator = null)
    {
        var viewModel = new SessionListViewModel(
            projection,
            recomputeCoordinator ?? TestCoordinatorSubstitutes.Session(),
            uiThreadDispatcher,
            pendingDeletes: TestPendingDeletes.Create(sessions: sessionCoordinator, shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());
        viewModel.LoadedCommand.Execute(null);
        return viewModel;
    }

    private static (IRecordedSessionProjection Projection, SourceCache<RecordedSessionSummary, Guid> Cache) CreateProjection()
    {
        var projection = Substitute.For<IRecordedSessionProjection>();
        var cache = new SourceCache<RecordedSessionSummary, Guid>(summary => summary.Id);
        projection.ConnectSessions().Returns(cache.Connect());
        return (projection, cache);
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
