using Sufni.App.Shell.Coordinators;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

using Sufni.App.LiveDaq.Stores;
using Sufni.App.LiveDaq.ViewModels.ItemLists;
using Sufni.App.LiveDaq.ViewModels.Rows;
using Sufni.App.Tests.TestSupport.Doubles;
namespace Sufni.App.Tests.LiveDaq.ViewModels.ItemLists;

public class LiveDaqListViewModelTests
{
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private static readonly InlineUiThreadDispatcher UiThreadDispatcher = new();

    [Fact]
    public void SearchText_FiltersItems_AcrossLiveDaqPresentationFields()
    {
        var store = new LiveDaqStore(new InlineUiThreadDispatcher());
        var coordinator = TestCoordinatorSubstitutes.LiveDaq();
        var viewModel = new LiveDaqListViewModel(store, coordinator, UiThreadDispatcher, timeProvider: new FakeTimeProvider(), shell: shell);
        viewModel.LoadedCommand.Execute(null);

        store.ReplaceAll([
            new LiveDaqSnapshot(
            IdentityKey: "board-alpha",
            DisplayName: "Alpha Board",
            BoardId: "board-alpha",
            Host: "192.168.0.10",
            Port: 1557,
            IsOnline: true,
            SetupName: "Race Setup",
            BikeName: "Trail Bike"),
            new LiveDaqSnapshot(
            IdentityKey: "board-beta",
            DisplayName: "Beta Board",
            BoardId: "board-beta",
            Host: "192.168.0.11",
            Port: 1666,
            IsOnline: false,
            SetupName: "Park Setup",
            BikeName: "Enduro Bike")]);

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
    public void Items_UpdateOnlyWhileLoaded_AndReloadCurrentStoreState()
    {
        using var store = new LiveDaqStore(new InlineUiThreadDispatcher());
        var coordinator = TestCoordinatorSubstitutes.LiveDaq();
        var firstBoard = new LiveDaqSnapshot(
            IdentityKey: "board-1",
            DisplayName: "Board 1",
            BoardId: "board-1",
            Host: "192.168.0.10",
            Port: 1557,
            IsOnline: true,
            SetupName: "Setup",
            BikeName: "Bike");
        store.ReplaceAll([firstBoard]);
        using var viewModel = new LiveDaqListViewModel(store, coordinator, UiThreadDispatcher, timeProvider: new FakeTimeProvider(), shell: shell);

        Assert.Empty(viewModel.Items);

        viewModel.LoadedCommand.Execute(null);
        Assert.Single(viewModel.Items);

        viewModel.UnloadedCommand.Execute(null);
        store.ReplaceAll([firstBoard, new LiveDaqSnapshot(
            IdentityKey: "board-2",
            DisplayName: "Board 2",
            BoardId: "board-2",
            Host: null,
            Port: null,
            IsOnline: false,
            SetupName: "Setup",
            BikeName: "Bike")]);
        Assert.Empty(viewModel.Items);

        viewModel.LoadedCommand.Execute(null);
        viewModel.LoadedCommand.Execute(null);
        Assert.Equal(2, viewModel.Items.Count);
    }

    [Fact]
    public async Task RowSelectedCommand_SelectsIdentityKey_WhenRowProvided()
    {
        var coordinator = TestCoordinatorSubstitutes.LiveDaq();
        var viewModel = new LiveDaqListViewModel(new LiveDaqStore(new InlineUiThreadDispatcher()), coordinator, UiThreadDispatcher, timeProvider: new FakeTimeProvider(), shell: shell);
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

        shell.Received(1).OpenEditor(new EditorKey.LiveDaq("board-1"));
    }

    [Fact]
    public async Task RowSelectedCommand_IgnoresOfflineRow()
    {
        var coordinator = TestCoordinatorSubstitutes.LiveDaq();
        var viewModel = new LiveDaqListViewModel(new LiveDaqStore(new InlineUiThreadDispatcher()), coordinator, UiThreadDispatcher, timeProvider: new FakeTimeProvider(), shell: shell);
        var offlineRow = new LiveDaqRowViewModel(new LiveDaqSnapshot(
            IdentityKey: "board-offline",
            DisplayName: "Offline Board",
            BoardId: "board-offline",
            Host: null,
            Port: null,
            IsOnline: false,
            SetupName: "Setup",
            BikeName: "Bike"));

        await viewModel.RowSelectedCommand.ExecuteAsync(offlineRow);

        shell.DidNotReceiveWithAnyArgs().OpenEditor(default!);
    }
}
