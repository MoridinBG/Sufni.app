using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;

using Sufni.App.Setups.ViewModels.ItemLists;
using Sufni.App.Setups.Views.ItemLists;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Setups.Views.ItemLists;

[Collection("Ui")]
public class SetupListViewTests
{
    [AvaloniaFact]
    public async Task SetupListView_RendersBoundRows_AndOpensSelectedSetup()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var snapshot = TestSnapshots.Setup(name: "Race Setup");
        var store = new SetupStoreStub(snapshot);
        var coordinator = TestCoordinatorSubstitutes.Setup();
        coordinator.OpenEditAsync(snapshot.Id).Returns(Task.CompletedTask);

        var viewModel = new SetupListViewModel(store, coordinator, new InlineUiThreadDispatcher());
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