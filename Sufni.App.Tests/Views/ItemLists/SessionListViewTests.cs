using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
        var row = Assert.Single(mounted.Control.FindAllVisual<SwipeToDeleteButton>());
        var openButton = row.FindControl<Button>("OpenButton");

        Assert.NotNull(openButton);
        openButton!.Command!.Execute(openButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        await coordinator.Received(1).OpenEditAsync(snapshot.Id);
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

        Assert.NotNull(deleteButton);
        Assert.NotNull(recalculateButton);
        Assert.Same(viewModel.Items[0].RecalculateCommand, recalculateButton!.Command);
        Assert.True(recalculateButton.Command!.CanExecute(recalculateButton.CommandParameter));
    }
}
