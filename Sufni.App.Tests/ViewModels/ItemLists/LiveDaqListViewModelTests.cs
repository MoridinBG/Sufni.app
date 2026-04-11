using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Stores;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.ViewModels.Rows;

namespace Sufni.App.Tests.ViewModels.ItemLists;

public class LiveDaqListViewModelTests
{
    [Fact]
    public void Activate_DelegatesToCoordinator()
    {
        var coordinator = Substitute.For<ILiveDaqCoordinator>();
        var viewModel = new LiveDaqListViewModel(new LiveDaqStore(), coordinator);

        viewModel.Activate();

        coordinator.Received(1).Activate();
    }

    [Fact]
    public void Deactivate_DelegatesToCoordinator()
    {
        var coordinator = Substitute.For<ILiveDaqCoordinator>();
        var viewModel = new LiveDaqListViewModel(new LiveDaqStore(), coordinator);

        viewModel.Deactivate();

        coordinator.Received(1).Deactivate();
    }

    [Fact]
    public void SearchText_FiltersItems_AcrossLiveDaqPresentationFields()
    {
        var store = new LiveDaqStore();
        var coordinator = Substitute.For<ILiveDaqCoordinator>();
        var viewModel = new LiveDaqListViewModel(store, coordinator);

        store.Upsert(new LiveDaqSnapshot(
            IdentityKey: "board-alpha",
            DisplayName: "Alpha Board",
            BoardId: "board-alpha",
            Host: "192.168.0.10",
            Port: 1557,
            IsOnline: true,
            SetupName: "Race Setup",
            BikeName: "Trail Bike"));
        store.Upsert(new LiveDaqSnapshot(
            IdentityKey: "board-beta",
            DisplayName: "Beta Board",
            BoardId: "board-beta",
            Host: "192.168.0.11",
            Port: 1666,
            IsOnline: false,
            SetupName: "Park Setup",
            BikeName: "Enduro Bike"));

        Assert.Equal(2, viewModel.Items.Count);

        viewModel.SearchText = "trail bike";
        Assert.Collection(viewModel.Items, row => Assert.Equal("board-alpha", row.IdentityKey));

        viewModel.SearchText = "board-beta";
        Assert.Collection(viewModel.Items, row => Assert.Equal("board-beta", row.IdentityKey));

        viewModel.SearchText = "192.168.0.10:1557";
        Assert.Collection(viewModel.Items, row => Assert.Equal("board-alpha", row.IdentityKey));

        viewModel.SearchText = null;
        Assert.Equal(2, viewModel.Items.Count);
    }

    [Fact]
    public async Task RowSelectedCommand_SelectsIdentityKey_WhenRowProvided()
    {
        var coordinator = Substitute.For<ILiveDaqCoordinator>();
        var viewModel = new LiveDaqListViewModel(new LiveDaqStore(), coordinator);
        var row = new LiveDaqRowViewModel(new LiveDaqSnapshot(
            IdentityKey: "board-1",
            DisplayName: "Board 1",
            BoardId: "board-1",
            Host: "192.168.0.50",
            Port: 1557,
            IsOnline: true,
            SetupName: "Race",
            BikeName: "Demo"));

        await viewModel.RowSelectedCommand.ExecuteAsync(row);

        await coordinator.Received(1).SelectAsync("board-1");
    }

    [Fact]
    public async Task RowSelectedCommand_DoesNothing_WhenRowIsNull()
    {
        var coordinator = Substitute.For<ILiveDaqCoordinator>();
        var viewModel = new LiveDaqListViewModel(new LiveDaqStore(), coordinator);

        await viewModel.RowSelectedCommand.ExecuteAsync(null);

        await coordinator.DidNotReceiveWithAnyArgs().SelectAsync(default!);
    }
}