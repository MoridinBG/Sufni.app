using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.Views.Controls;
using Sufni.App.Views.ItemLists;

namespace Sufni.App.Tests.Views.ItemLists;

public class SetupListViewTests
{
    [AvaloniaFact]
    public async Task SetupListView_RendersBoundRows_AndOpensSelectedSetup()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var snapshot = TestSnapshots.Setup(name: "Race Setup");
        var store = new SetupStoreStub(snapshot);
        var coordinator = Substitute.For<ISetupCoordinator>();
        coordinator.OpenEditAsync(snapshot.Id).Returns(Task.CompletedTask);

        var viewModel = new SetupListViewModel(store, coordinator);
        var view = new SetupListView
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
}