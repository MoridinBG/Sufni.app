using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Stores;
using Sufni.App.LiveDaq.ViewModels.ItemLists;
using Sufni.App.LiveDaq.Views.ItemLists;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.LiveDaq.Views.ItemLists;

[Collection("Ui")]
public class LiveDaqListViewTests
{
    [AvaloniaFact]
    public async Task LiveDaqListView_RendersOnlineRowAndInvokesSelection()
    {
        var coordinator = TestCoordinatorSubstitutes.LiveDaq();
        var store = new LiveDaqStore();
        store.Upsert(new LiveDaqSnapshot(
            IdentityKey: "board-1",
            DisplayName: "Board 1",
            BoardId: "board-id-xyz",
            Host: "192.168.0.30",
            Port: 1557,
            IsOnline: true,
            SetupName: "Race",
            BikeName: "Demo"));

        var viewModel = new LiveDaqListViewModel(store, coordinator, new InlineUiThreadDispatcher());
        await using var mounted = await MountAsync(viewModel);

        var row = Assert.Single(FindRowButtons(mounted.Control));
        Assert.Equal("Board 1", FindNamed<TextBlock>(row, "DisplayNameTextBlock").Text);
        Assert.Equal("Race", FindNamed<TextBlock>(row, "SetupNameTextBlock").Text);
        Assert.Equal("Demo", FindNamed<TextBlock>(row, "BikeNameTextBlock").Text);
        Assert.True(FindNamed<Border>(row, "OnlineBadge").IsVisible);

        row.Command!.Execute(row.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        await coordinator.Received(1).SelectAsync("board-1");
    }

    private static Button[] FindRowButtons(Control root)
    {
        return root.FindAllVisual<Button>()
            .Where(b => b.Name == "OpenButton")
            .ToArray();
    }

    private static T FindNamed<T>(Control root, string name) where T : Control
    {
        return root.GetVisualDescendants()
            .OfType<T>()
            .Single(c => c.Name == name);
    }

    private static async Task<MountedInMainPagesHost<LiveDaqListView>> MountAsync(LiveDaqListViewModel viewModel)
    {
        var view = new LiveDaqListView
        {
            DataContext = viewModel
        };
        return await ListHostTestSupport.MountInSharedMainPagesHostAsync(view);
    }
}
