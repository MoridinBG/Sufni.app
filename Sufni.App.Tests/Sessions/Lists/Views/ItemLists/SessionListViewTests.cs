using Sufni.App.Shell.Coordinators;
using Microsoft.Extensions.Time.Testing;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Labs.Controls;
using Avalonia.VisualTree;
using DynamicData;
using NSubstitute;

using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Lists.DesktopViews.Controls;
using Sufni.App.Sessions.Lists.DesktopViews.ItemLists;
using Sufni.App.Sessions.Lists.ViewModels.ItemLists;
using Sufni.App.Sessions.Lists.ViewModels.Rows;
using Sufni.App.Sessions.Lists.Views.Controls;
using Sufni.App.Sessions.Lists.Views.ItemLists;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Shell.Views.Controls;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Video;
namespace Sufni.App.Tests.Sessions.Lists.Views.ItemLists;

[Collection("Ui")]
public class SessionListViewTests
{
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    [AvaloniaFact]
    public async Task DesktopImportButton_UsesTheListCommandWithoutAParentShellView()
    {
        ViewTestHelpers.EnsureViewTestResources();
        using var cache = new SourceCache<RecordedSessionSummary, Guid>(summary => summary.Id);
        var projection = Substitute.For<IRecordedSessionProjection>();
        projection.ConnectSessions().Returns(cache.Connect());
        var coordinator = TestCoordinatorSubstitutes.Session();
        using var pending = TestPendingDeletes.Create(sessions: coordinator, shell: shell);
        using var viewModel = new SessionListViewModel(projection, coordinator, VideoFixture.CreateEmptyStore(),
            new InlineUiThreadDispatcher(), pending, new FakeTimeProvider(), shell, TestShellFeedback.Create());
        var view = new SessionListDesktopView { DataContext = viewModel };
        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            var button = view.FindControl<Button>("ImportButton")!;
            Assert.True(button.Command!.CanExecute(null));
            button.Command.Execute(null);
            await ViewTestHelpers.FlushDispatcherAsync();

            shell.Received(1).OpenEditor(new EditorKey.ImportSessions());
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task SessionListView_RendersDateFilterBar_AndOpensSelectedSession()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var snapshot = TestSnapshots.Session(name: "Morning Ride", timestamp: 1_700_000_000, hasProcessedData: false);
        using var cache = new SourceCache<RecordedSessionSummary, Guid>(summary => summary.Id);
        cache.AddOrUpdate(new RecordedSessionSummary(
            snapshot.Id,
            snapshot.Updated,
            snapshot.Name,
            snapshot.Description,
            snapshot.Timestamp,
            snapshot.HasProcessedData,
            new SessionStaleness.MissingProcessedData()));
        var projection = Substitute.For<IRecordedSessionProjection>();
        projection.ConnectSessions().Returns(cache.Connect());
        var coordinator = TestCoordinatorSubstitutes.Session();

        var viewModel = new SessionListViewModel(projection, coordinator, VideoFixture.CreateEmptyStore(), new InlineUiThreadDispatcher(), pendingDeletes: TestPendingDeletes.Create(sessions: coordinator, shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());
        var view = new SessionListView
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        Assert.NotNull(mounted.Control.FindFirstVisual<PullableMenuScrollViewer>());
        Assert.NotNull(mounted.Control.FindNamedVisual<Border>("DatePickers"));
        var row = Assert.Single(mounted.Control.FindAllVisual<SessionSwipeActionButton>());
        var header = Assert.Single(
            mounted.Control.FindAllVisual<TextBlock>(),
            text => text.Name == "DateGroupHeaderText");
        var rowViewModel = Assert.IsType<SessionRowViewModel>(row.DataContext);

        Assert.Equal("Morning Ride", rowViewModel.BaseName);
        Assert.Equal(viewModel.DateGroups[0].HeaderText, header.Text);
        rowViewModel.OpenPageCommand.Execute(null);
        await ViewTestHelpers.FlushDispatcherAsync();

        shell.Received(1).OpenEditor(new EditorKey.Session(snapshot.Id));
    }

    [AvaloniaFact]
    public async Task SessionListView_ShowsMissingVideoIndicator_WhenLinkedFileIsGone()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var snapshot = TestSnapshots.Session(name: "Morning Ride", timestamp: 1_700_000_000);
        using var cache = new SourceCache<RecordedSessionSummary, Guid>(summary => summary.Id);
        cache.AddOrUpdate(new RecordedSessionSummary(
            snapshot.Id,
            snapshot.Updated,
            snapshot.Name,
            snapshot.Description,
            snapshot.Timestamp,
            snapshot.HasProcessedData,
            new SessionStaleness.Current()));
        var projection = Substitute.For<IRecordedSessionProjection>();
        projection.ConnectSessions().Returns(cache.Connect());
        using var videos = new VideoFixture();
        var videoStore = await videos.CreateStoreAsync(
            videos.CreateClip(snapshot.Id, startTimestamp: 0, durationSeconds: 60, fileExists: false));
        var viewModel = new SessionListViewModel(
            projection,
            TestCoordinatorSubstitutes.Session(),
            videoStore,
            new InlineUiThreadDispatcher(), pendingDeletes: TestPendingDeletes.Create(sessions: TestCoordinatorSubstitutes.Session(), shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());
        var view = new SessionListView
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        var row = Assert.Single(mounted.Control.FindAllVisual<SessionSwipeActionButton>());
        Assert.True(row.FindControl<Image>("MissingVideoIndicator")!.IsVisible);
        Assert.False(row.FindControl<Image>("PresentVideoIndicator")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionListView_SessionSwipeActions_InvokeRecalculateAndDelete()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var snapshot = TestSnapshots.Session(name: "Morning Ride", timestamp: 1_700_000_000);
        using var cache = new SourceCache<RecordedSessionSummary, Guid>(summary => summary.Id);
        cache.AddOrUpdate(new RecordedSessionSummary(
            snapshot.Id,
            snapshot.Updated,
            snapshot.Name,
            snapshot.Description,
            snapshot.Timestamp,
            snapshot.HasProcessedData,
            new SessionStaleness.DependencyHashChanged()));
        var projection = Substitute.For<IRecordedSessionProjection>();
        projection.ConnectSessions().Returns(cache.Connect());
        var coordinator = TestCoordinatorSubstitutes.Session();
        coordinator.RequestRecomputeAsync(snapshot.Id, RecomputeReason.ManualFromList)
            .Returns(new SessionRecomputeResult.Recomputed(snapshot.Updated + 1));

        var viewModel = new SessionListViewModel(projection, coordinator, VideoFixture.CreateEmptyStore(), new InlineUiThreadDispatcher(), pendingDeletes: TestPendingDeletes.Create(sessions: coordinator, shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());
        var view = new SessionListView
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        var row = Assert.Single(mounted.Control.FindAllVisual<SessionSwipeActionButton>());
        var swipe = row.FindControl<Swipe>("SwipeButton");

        Assert.NotNull(swipe);

        swipe!.SwipeState = SwipeState.LeftVisible;
        await ViewTestHelpers.FlushDispatcherAsync();

        await coordinator.Received(1).RequestRecomputeAsync(snapshot.Id, RecomputeReason.ManualFromList);
        Assert.Equal(SwipeState.Hidden, swipe.SwipeState);

        swipe.SwipeState = SwipeState.RightVisible;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Empty(viewModel.Items);
        Assert.Single(viewModel.PendingDeletes);
        Assert.Equal(SwipeState.Hidden, swipe.SwipeState);
    }

    [AvaloniaFact]
    public async Task SessionListDesktopView_RendersRecalculateButton_UsingRowCommand()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var snapshot = TestSnapshots.Session(name: "Morning Ride", timestamp: 1_700_000_000);
        using var cache = new SourceCache<RecordedSessionSummary, Guid>(summary => summary.Id);
        cache.AddOrUpdate(new RecordedSessionSummary(
            snapshot.Id,
            snapshot.Updated,
            snapshot.Name,
            snapshot.Description,
            snapshot.Timestamp,
            snapshot.HasProcessedData,
            new SessionStaleness.DependencyHashChanged()));
        var projection = Substitute.For<IRecordedSessionProjection>();
        projection.ConnectSessions().Returns(cache.Connect());

        var viewModel = new SessionListViewModel(projection, TestCoordinatorSubstitutes.Session(), VideoFixture.CreateEmptyStore(), new InlineUiThreadDispatcher(), pendingDeletes: TestPendingDeletes.Create(sessions: TestCoordinatorSubstitutes.Session(), shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());
        var view = new SessionListDesktopView
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        var row = Assert.Single(mounted.Control.FindAllVisual<SessionListItemButton>());
        var deleteButton = row.FindControl<Button>("DeleteButton");
        var recalculateButton = row.FindControl<Button>("RecalculateButton");

        Assert.NotNull(deleteButton);
        Assert.NotNull(recalculateButton);
        Assert.Same(viewModel.Items[0].RecalculateCommand, recalculateButton!.Command);
        Assert.True(recalculateButton.Command!.CanExecute(recalculateButton.CommandParameter));
    }

    [AvaloniaFact]
    public async Task SessionListDesktopView_ShowsVideoIndicator_WhenSessionHasLinkedVideo()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var snapshot = TestSnapshots.Session(name: "Morning Ride", timestamp: 1_700_000_000);
        using var cache = new SourceCache<RecordedSessionSummary, Guid>(summary => summary.Id);
        cache.AddOrUpdate(new RecordedSessionSummary(
            snapshot.Id,
            snapshot.Updated,
            snapshot.Name,
            snapshot.Description,
            snapshot.Timestamp,
            snapshot.HasProcessedData,
            new SessionStaleness.Current()));
        var projection = Substitute.For<IRecordedSessionProjection>();
        projection.ConnectSessions().Returns(cache.Connect());
        using var videos = new VideoFixture();
        var videoStore = await videos.CreateStoreAsync(
            videos.CreateClip(snapshot.Id, startTimestamp: 0, durationSeconds: 60, fileExists: true));
        var viewModel = new SessionListViewModel(
            projection,
            TestCoordinatorSubstitutes.Session(),
            videoStore,
            new InlineUiThreadDispatcher(), pendingDeletes: TestPendingDeletes.Create(sessions: TestCoordinatorSubstitutes.Session(), shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());
        var view = new SessionListDesktopView
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        var row = Assert.Single(mounted.Control.FindAllVisual<SessionListItemButton>());
        Assert.True(row.FindControl<Image>("PresentVideoIndicator")!.IsVisible);
        Assert.False(row.FindControl<Image>("MissingVideoIndicator")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionListDesktopView_RendersGroupedHeaders_AndCollapseTogglesRows()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var first = TestSnapshots.Session(name: "Morning Ride", timestamp: ToUnixSeconds(2026, 5, 20, 9, 15));
        var second = TestSnapshots.Session(name: "Evening Ride", timestamp: ToUnixSeconds(2026, 5, 19, 18, 30));
        using var cache = new SourceCache<RecordedSessionSummary, Guid>(summary => summary.Id);
        cache.AddOrUpdate(new RecordedSessionSummary(
            first.Id,
            first.Updated,
            first.Name,
            first.Description,
            first.Timestamp,
            first.HasProcessedData,
            new SessionStaleness.Current()));
        cache.AddOrUpdate(new RecordedSessionSummary(
            second.Id,
            second.Updated,
            second.Name,
            second.Description,
            second.Timestamp,
            second.HasProcessedData,
            new SessionStaleness.Current()));
        var projection = Substitute.For<IRecordedSessionProjection>();
        projection.ConnectSessions().Returns(cache.Connect());

        var viewModel = new SessionListViewModel(projection, TestCoordinatorSubstitutes.Session(), VideoFixture.CreateEmptyStore(), new InlineUiThreadDispatcher(), pendingDeletes: TestPendingDeletes.Create(sessions: TestCoordinatorSubstitutes.Session(), shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());
        var view = new SessionListDesktopView
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        var headers = mounted.Control.FindAllVisual<TextBlock>()
            .Where(text => text.Name == "DateGroupHeaderText")
            .ToList();
        var headerButtons = mounted.Control.FindAllVisual<Button>()
            .Where(button => button.Name == "DateGroupHeaderButton")
            .ToList();
        var groupRepeaters = mounted.Control.FindAllVisual<ItemsRepeater>()
            .Where(repeater => repeater.Name == "DateGroupItemsRepeater")
            .ToList();

        Assert.Equal(2, headers.Count);
        Assert.Equal(viewModel.DateGroups[0].HeaderText, headers[0].Text);
        Assert.Equal(2, headerButtons.Count);
        Assert.Equal(2, groupRepeaters.Count);
        Assert.All(groupRepeaters, repeater => Assert.True(repeater.IsVisible));

        headerButtons[0].Command!.Execute(headerButtons[0].CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(viewModel.DateGroups[0].IsExpanded);
        Assert.False(groupRepeaters[0].IsVisible);
        Assert.True(groupRepeaters[1].IsVisible);
    }

    private static long ToUnixSeconds(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local);
        return new DateTimeOffset(local).ToUnixTimeSeconds();
    }
}
