using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Queries;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.Views.Controls;
using Sufni.App.Views.ItemLists;

namespace Sufni.App.Tests.Views.ItemLists;

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

        var viewModel = new BikeListViewModel(store, coordinator, dependencyQuery);
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
            DataContext = new BikeListViewModel(store, coordinator, dependencyQuery),
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);

        Assert.Empty(mounted.Control.FindAllVisual<SwipeToDeleteButton>());
    }
}