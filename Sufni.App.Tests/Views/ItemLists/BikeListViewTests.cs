using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Tests.TestSupport;

using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.ViewModels.ItemLists;
using Sufni.App.Bikes.Views.ItemLists;
using Sufni.App.Shared.Views.Controls;
namespace Sufni.App.Tests.Views.ItemLists;

[Collection("Ui")]
public class BikeListViewTests
{
    [AvaloniaFact]
    public async Task BikeListView_RendersBoundRows_AndOpensSelectedBike()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var snapshot = TestSnapshots.Bike(name: "Trail Bike");
        var store = new BikeStoreStub(snapshot);
        var coordinator = TestCoordinatorSubstitutes.Bike();
        coordinator.OpenEditAsync(snapshot.Id).Returns(Task.CompletedTask);
        var dependencyQuery = Substitute.For<IBikeDependencyQuery>();
        dependencyQuery.IsBikeInUse(snapshot.Id).Returns(false);
        dependencyQuery.Changes.Returns(Observable.Return(Unit.Default));

        var viewModel = new BikeListViewModel(store, coordinator, dependencyQuery, new InlineUiThreadDispatcher());
        var view = new BikeListView
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        Assert.NotNull(mounted.Control.FindFirstVisual<SearchBar>());
        var row = Assert.Single(mounted.Control.FindAllVisual<SwipeToDeleteButton>());
        var openButton = row.FindControl<Button>("OpenButton");

        Assert.NotNull(openButton);
        openButton!.Command!.Execute(openButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        await coordinator.Received(1).OpenEditAsync(snapshot.Id);
    }

    [AvaloniaFact]
    public async Task BikeListView_RendersNoRows_WhenStoreIsEmpty()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var store = new BikeStoreStub();
        var coordinator = TestCoordinatorSubstitutes.Bike();
        var dependencyQuery = Substitute.For<IBikeDependencyQuery>();
        dependencyQuery.Changes.Returns(Observable.Return(Unit.Default));

        var view = new BikeListView
        {
            DataContext = new BikeListViewModel(store, coordinator, dependencyQuery, new InlineUiThreadDispatcher()),
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        Assert.Empty(mounted.Control.FindAllVisual<SwipeToDeleteButton>());
    }
}
