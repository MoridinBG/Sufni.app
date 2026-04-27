using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.Views.ItemLists;

namespace Sufni.App.Tests.Views;

public class LiveDaqListViewTests
{
    [AvaloniaFact]
    public async Task LiveDaqListView_RendersRowsWithBoundFields()
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

        var viewModel = new LiveDaqListViewModel(store, coordinator);
        await using var mounted = await MountAsync(viewModel);

        var row = Assert.Single(FindRowButtons(mounted.Control));
        Assert.Equal("Board 1", FindNamed<TextBlock>(row, "DisplayNameTextBlock").Text);
        Assert.Equal("Race", FindNamed<TextBlock>(row, "SetupNameTextBlock").Text);
        Assert.Equal("Demo", FindNamed<TextBlock>(row, "BikeNameTextBlock").Text);
        Assert.Equal("board-id-xyz", FindNamed<TextBlock>(row, "BoardIdTextBlock").Text);
        Assert.True(FindNamed<TextBlock>(row, "BoardIdTextBlock").IsVisible);
        Assert.Equal("192.168.0.30:1557", FindNamed<TextBlock>(row, "EndpointTextBlock").Text);
        Assert.True(FindNamed<TextBlock>(row, "EndpointTextBlock").IsVisible);
        Assert.True(FindNamed<Border>(row, "OnlineBadge").IsVisible);
        Assert.False(FindNamed<Border>(row, "OfflineBadge").IsVisible);
    }

    [AvaloniaFact]
    public async Task LiveDaqListView_HidesBoardIdAndEndpoint_WhenSameAsDisplayName()
    {
        var coordinator = TestCoordinatorSubstitutes.LiveDaq();
        var store = new LiveDaqStore();
        store.Upsert(new LiveDaqSnapshot(
            IdentityKey: "board-1",
            DisplayName: "192.168.0.30:1557",
            BoardId: "192.168.0.30:1557",
            Host: "192.168.0.30",
            Port: 1557,
            IsOnline: true,
            SetupName: null,
            BikeName: null));

        var viewModel = new LiveDaqListViewModel(store, coordinator);
        await using var mounted = await MountAsync(viewModel);

        var row = Assert.Single(FindRowButtons(mounted.Control));
        Assert.False(FindNamed<TextBlock>(row, "BoardIdTextBlock").IsVisible);
        Assert.False(FindNamed<TextBlock>(row, "EndpointTextBlock").IsVisible);
    }

    [AvaloniaFact]
    public async Task LiveDaqListView_OfflineRow_IsDisabled_AndShowsOfflineBadgeAndFade()
    {
        var coordinator = TestCoordinatorSubstitutes.LiveDaq();
        var store = new LiveDaqStore();
        store.Upsert(new LiveDaqSnapshot(
            IdentityKey: "board-1",
            DisplayName: "Board 1",
            BoardId: "board-1",
            Host: null,
            Port: null,
            IsOnline: false,
            SetupName: null,
            BikeName: null));

        var viewModel = new LiveDaqListViewModel(store, coordinator);
        await using var mounted = await MountAsync(viewModel);

        var row = Assert.Single(FindRowButtons(mounted.Control));
        Assert.False(row.IsEnabled);
        Assert.Contains("offline", row.Classes);
        Assert.False(FindNamed<Border>(row, "OnlineBadge").IsVisible);
        Assert.True(FindNamed<Border>(row, "OfflineBadge").IsVisible);
    }

    [AvaloniaFact]
    public async Task LiveDaqListView_RowTap_InvokesRowSelectedCommand_WithRow()
    {
        var coordinator = TestCoordinatorSubstitutes.LiveDaq();
        var store = new LiveDaqStore();
        store.Upsert(new LiveDaqSnapshot(
            IdentityKey: "board-1",
            DisplayName: "Board 1",
            BoardId: "board-1",
            Host: "192.168.0.30",
            Port: 1557,
            IsOnline: true,
            SetupName: null,
            BikeName: null));

        var viewModel = new LiveDaqListViewModel(store, coordinator);
        await using var mounted = await MountAsync(viewModel);

        var row = Assert.Single(FindRowButtons(mounted.Control));
        row.Command!.Execute(row.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        await coordinator.Received(1).SelectAsync("board-1");
    }

    [AvaloniaFact]
    public async Task LiveDaqListView_OfflineRowTap_DoesNotInvokeCoordinator()
    {
        var coordinator = TestCoordinatorSubstitutes.LiveDaq();
        var store = new LiveDaqStore();
        store.Upsert(new LiveDaqSnapshot(
            IdentityKey: "board-1",
            DisplayName: "Board 1",
            BoardId: "board-1",
            Host: null,
            Port: null,
            IsOnline: false,
            SetupName: null,
            BikeName: null));

        var viewModel = new LiveDaqListViewModel(store, coordinator);

        // Bypass IsEnabled to confirm the VM-level guard rejects offline rows.
        var row = viewModel.Items.Single();
        viewModel.RowSelectedCommand.Execute(row);
        await ViewTestHelpers.FlushDispatcherAsync();

        await coordinator.DidNotReceive().SelectAsync(Arg.Any<string>());
    }

    [AvaloniaFact]
    public async Task LiveDaqListView_SearchTextFilter_HidesNonMatchingRows()
    {
        var coordinator = TestCoordinatorSubstitutes.LiveDaq();
        var store = new LiveDaqStore();
        store.Upsert(new LiveDaqSnapshot(
            IdentityKey: "alpha",
            DisplayName: "Alpha",
            BoardId: "alpha",
            Host: "192.168.0.30",
            Port: 1557,
            IsOnline: true,
            SetupName: null,
            BikeName: null));
        store.Upsert(new LiveDaqSnapshot(
            IdentityKey: "beta",
            DisplayName: "Beta",
            BoardId: "beta",
            Host: "192.168.0.31",
            Port: 1557,
            IsOnline: true,
            SetupName: null,
            BikeName: null));

        var viewModel = new LiveDaqListViewModel(store, coordinator);
        await using var mounted = await MountAsync(viewModel);

        Assert.Equal(2, FindRowButtons(mounted.Control).Length);

        viewModel.SearchText = "alp";
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Single(viewModel.Items);
        Assert.Equal("Alpha", viewModel.Items[0].DisplayName);
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
