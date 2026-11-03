using Sufni.App.Video.Stores;
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
        var recomputeCoordinator = TestCoordinatorSubstitutes.Session();
        using var pending = TestPendingDeletes.Create(sessions: coordinator, shell: shell);
        using var viewModel = new SessionListViewModel(projection, recomputeCoordinator,
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
        cache.AddOrUpdate(cache.Lookup(snapshot.Id).Value with { LatestVideo =
            videos.CreateClip("source-hash", startTimestamp: 0, durationSeconds: 60, fileExists: false) });
        using var pending = TestPendingDeletes.Create(sessions: TestCoordinatorSubstitutes.Session(), shell: shell);
        using var viewModel = new SessionListViewModel(
            projection,
            TestCoordinatorSubstitutes.Session(),
            new InlineUiThreadDispatcher(), pendingDeletes: pending, timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());
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
            new SessionStaleness.Stale()));
        var projection = Substitute.For<IRecordedSessionProjection>();
        projection.ConnectSessions().Returns(cache.Connect());

        var coordinator = TestCoordinatorSubstitutes.Session();
        using var pending = TestPendingDeletes.Create(sessions: TestCoordinatorSubstitutes.Session(), shell: shell);
        using var viewModel = new SessionListViewModel(projection, coordinator, new InlineUiThreadDispatcher(), pendingDeletes: pending, timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());
        var view = new SessionListDesktopView
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        var row = Assert.Single(mounted.Control.FindAllVisual<SessionListItemButton>());
        var recalculateButton = row.FindControl<Button>("RecalculateButton");

        Assert.NotNull(recalculateButton);
        Assert.True(recalculateButton!.Command!.CanExecute(recalculateButton.CommandParameter));
        recalculateButton.Command.Execute(recalculateButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        await coordinator.Received(1).RecomputeAsync(snapshot.Id);
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
        cache.AddOrUpdate(cache.Lookup(snapshot.Id).Value with { LatestVideo =
            videos.CreateClip("source-hash", startTimestamp: 0, durationSeconds: 60, fileExists: true) });
        using var pending = TestPendingDeletes.Create(sessions: TestCoordinatorSubstitutes.Session(), shell: shell);
        using var viewModel = new SessionListViewModel(
            projection,
            TestCoordinatorSubstitutes.Session(),
            new InlineUiThreadDispatcher(), pendingDeletes: pending, timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());
        var view = new SessionListDesktopView
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        var row = Assert.Single(mounted.Control.FindAllVisual<SessionListItemButton>());
        Assert.True(row.FindControl<Image>("PresentVideoIndicator")!.IsVisible);
        Assert.False(row.FindControl<Image>("MissingVideoIndicator")!.IsVisible);
    }

}
