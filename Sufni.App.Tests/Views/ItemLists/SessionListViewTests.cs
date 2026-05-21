using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Labs.Controls;
using DynamicData;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.DesktopViews.Controls;
using Sufni.App.DesktopViews.ItemLists;
using Sufni.App.SessionGraph;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.Views.Controls;
using Sufni.App.Views.ItemLists;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Sufni.App.Tests.Views.ItemLists;

public class SessionListViewTests
{
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
        var graph = Substitute.For<IRecordedSessionGraph>();
        graph.ConnectSessions().Returns(cache.Connect());
        var coordinator = TestCoordinatorSubstitutes.Session();
        coordinator.OpenEditAsync(snapshot.Id).Returns(Task.CompletedTask);

        var viewModel = new SessionListViewModel(graph, coordinator);
        var view = new SessionListView
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        Assert.NotNull(mounted.Control.FindFirstVisual<SearchBarWithDateFilter>());
        var row = Assert.Single(mounted.Control.FindAllVisual<SessionSwipeActionButton>());
        var header = Assert.Single(
            mounted.Control.FindAllVisual<TextBlock>(),
            text => text.Name == "DateGroupHeaderText");
        var groupRepeater = Assert.Single(
            mounted.Control.FindAllVisual<ItemsRepeater>(),
            repeater => repeater.Name == "DateGroupItemsRepeater");
        var collapsedGlyph = Assert.Single(
            mounted.Control.FindAllVisual<ShapePath>(),
            path => path.Name == "DateGroupCollapsedGlyph");
        var expandedGlyph = Assert.Single(
            mounted.Control.FindAllVisual<ShapePath>(),
            path => path.Name == "DateGroupExpandedGlyph");
        var openButton = row.FindControl<Button>("OpenButton");

        Assert.NotNull(openButton);
        Assert.Equal(viewModel.DateGroups[0].HeaderText, header.Text);
        Assert.Equal(18, groupRepeater.Margin.Left);
        Assert.NotNull(collapsedGlyph.Data);
        Assert.NotNull(expandedGlyph.Data);
        openButton!.Command!.Execute(openButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        await coordinator.Received(1).OpenEditAsync(snapshot.Id);
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
        var graph = Substitute.For<IRecordedSessionGraph>();
        graph.ConnectSessions().Returns(cache.Connect());
        var coordinator = TestCoordinatorSubstitutes.Session();
        coordinator.RecomputeAsync(snapshot.Id, snapshot.Updated, Arg.Any<CancellationToken>())
            .Returns(new SessionRecomputeResult.Recomputed(snapshot.Updated + 1));

        var viewModel = new SessionListViewModel(graph, coordinator);
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

        await coordinator.Received(1).RecomputeAsync(snapshot.Id, snapshot.Updated, Arg.Any<CancellationToken>());
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
        var graph = Substitute.For<IRecordedSessionGraph>();
        graph.ConnectSessions().Returns(cache.Connect());

        var viewModel = new SessionListViewModel(graph, TestCoordinatorSubstitutes.Session());
        var view = new SessionListDesktopView
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        var row = Assert.Single(mounted.Control.FindAllVisual<SessionListItemButton>());
        var deleteButton = row.FindControl<Button>("DeleteButton");
        var recalculateButton = row.FindControl<Button>("RecalculateButton");
        var titleText = row.FindControl<TextBlock>("TitleTextBlock");
        var timestampText = row.FindControl<TextBlock>("TimestampTextBlock");

        Assert.NotNull(deleteButton);
        Assert.NotNull(recalculateButton);
        Assert.NotNull(titleText);
        Assert.NotNull(timestampText);
        Assert.Equal("Morning Ride (Stale)", titleText!.Text);
        Assert.Equal(viewModel.Items[0].TimestampText, timestampText!.Text);
        Assert.Same(viewModel.Items[0].RecalculateCommand, recalculateButton!.Command);
        Assert.True(recalculateButton.Command!.CanExecute(recalculateButton.CommandParameter));
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
        var graph = Substitute.For<IRecordedSessionGraph>();
        graph.ConnectSessions().Returns(cache.Connect());

        var viewModel = new SessionListViewModel(graph, TestCoordinatorSubstitutes.Session());
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
        var collapsedGlyphs = mounted.Control.FindAllVisual<ShapePath>()
            .Where(path => path.Name == "DateGroupCollapsedGlyph")
            .ToList();
        var expandedGlyphs = mounted.Control.FindAllVisual<ShapePath>()
            .Where(path => path.Name == "DateGroupExpandedGlyph")
            .ToList();

        Assert.Equal(2, headers.Count);
        Assert.Equal(viewModel.DateGroups[0].HeaderText, headers[0].Text);
        Assert.Equal(2, headerButtons.Count);
        Assert.Equal(2, groupRepeaters.Count);
        Assert.Equal(2, collapsedGlyphs.Count);
        Assert.Equal(2, expandedGlyphs.Count);
        Assert.All(groupRepeaters, repeater => Assert.Equal(18, repeater.Margin.Left));
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
