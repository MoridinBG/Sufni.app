using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Labs.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using DynamicData;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionGraph;

using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Lists.DesktopViews.Controls;
using Sufni.App.Sessions.Lists.DesktopViews.ItemLists;
using Sufni.App.Sessions.Lists.ViewModels.ItemLists;
using Sufni.App.Sessions.Lists.Views.Controls;
using Sufni.App.Sessions.Lists.Views.ItemLists;
using Sufni.App.Sessions.Processing.SessionGraph;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Sessions.Lists.Views.ItemLists;

[Collection("Ui")]
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

        var viewModel = new SessionListViewModel(graph, coordinator, new InlineUiThreadDispatcher());
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
        var openButton = row.FindControl<Button>("OpenButton");

        Assert.NotNull(openButton);
        Assert.Equal(viewModel.DateGroups[0].HeaderText, header.Text);
        openButton!.Command!.Execute(openButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        await coordinator.Received(1).OpenEditAsync(snapshot.Id);
    }

    [AvaloniaFact]
    public async Task SessionListView_RendersExtensionIndicators()
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
        var graph = Substitute.For<IRecordedSessionGraph>();
        graph.ConnectSessions().Returns(cache.Connect());
        var viewModel = new SessionListViewModel(
            graph,
            TestCoordinatorSubstitutes.Session(),
            new InlineUiThreadDispatcher(),
            new TestRecordedSessionListExtensionService());
        var view = new SessionListView
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        AssertContributionText(mounted.Control, "SessionListIndicator", "Indicator");
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
        coordinator.RequestRecomputeAsync(snapshot.Id, RecomputeReason.ManualFromList)
            .Returns(new SessionRecomputeResult.Recomputed(snapshot.Updated + 1));

        var viewModel = new SessionListViewModel(graph, coordinator, new InlineUiThreadDispatcher());
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
        var graph = Substitute.For<IRecordedSessionGraph>();
        graph.ConnectSessions().Returns(cache.Connect());

        var viewModel = new SessionListViewModel(graph, TestCoordinatorSubstitutes.Session(), new InlineUiThreadDispatcher());
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
    public async Task SessionListDesktopView_RendersExtensionIndicatorsAndActions()
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
        var graph = Substitute.For<IRecordedSessionGraph>();
        graph.ConnectSessions().Returns(cache.Connect());
        var viewModel = new SessionListViewModel(
            graph,
            TestCoordinatorSubstitutes.Session(),
            new InlineUiThreadDispatcher(),
            new TestRecordedSessionListExtensionService());
        var view = new SessionListDesktopView
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        var row = Assert.Single(mounted.Control.FindAllVisual<SessionListItemButton>());

        AssertContributionText(mounted.Control, "SessionListIndicator", "Indicator");
        AssertLogicalContributionText(row, "SessionListAction", "Action");
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

        var viewModel = new SessionListViewModel(graph, TestCoordinatorSubstitutes.Session(), new InlineUiThreadDispatcher());
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

    private static void AssertContributionText(Control root, string name, string text)
    {
        var textBlocks = root.GetVisualDescendants()
            .OfType<TextBlock>()
            .ToArray();
        var textBlock = textBlocks.SingleOrDefault(textBlock => textBlock.Name == name);
        Assert.True(
            textBlock is not null,
            $"Expected contribution text '{name}'. Actual text blocks: {string.Join(", ", textBlocks.Select(block => $"{block.Name}:{block.Text}"))}");
        Assert.Equal(text, textBlock!.Text);
    }

    private static void AssertLogicalContributionText(Control root, string name, string text)
    {
        var textBlocks = root.GetLogicalDescendants()
            .OfType<TextBlock>()
            .ToArray();
        var textBlock = textBlocks.SingleOrDefault(textBlock => textBlock.Name == name);
        Assert.True(
            textBlock is not null,
            $"Expected contribution text '{name}'. Actual text blocks: {string.Join(", ", textBlocks.Select(block => $"{block.Name}:{block.Text}"))}");
        Assert.Equal(text, textBlock!.Text);
    }

}
