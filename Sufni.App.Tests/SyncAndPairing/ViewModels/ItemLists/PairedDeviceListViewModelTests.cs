using DynamicData;
using NSubstitute;

using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.SyncAndPairing.ViewModels.ItemLists;
using Sufni.App.Shared.Stores;
namespace Sufni.App.Tests.SyncAndPairing.ViewModels.ItemLists;

public class PairedDeviceListViewModelTests
{
    private static readonly InlineUiThreadDispatcher UiThreadDispatcher = new();

    [Fact]
    public async Task FinalizeUnpair_KeepsDeviceHidden_WhileDeleteInProgress()
    {
        var snapshot = new PairedDeviceSnapshot(
            DeviceId: "device-1",
            DisplayName: "Phone",
            Expires: new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc));

        using var pairedDeviceCache = new SourceCache<PairedDeviceSnapshot, string>(s => s.DeviceId);
        pairedDeviceCache.AddOrUpdate(snapshot);

        var pairedDeviceStore = Substitute.For<IPairedDeviceStore>();
        pairedDeviceStore.Connect().Returns(pairedDeviceCache.Connect());
        pairedDeviceStore.Get(snapshot.DeviceId).Returns(snapshot);

        var storeWriter = Substitute.For<IPairedDeviceStoreWriter>();
        var deleteTcs = new TaskCompletionSource();
        storeWriter.CommitLocalUnpairAsync(snapshot.DeviceId, Arg.Any<CancellationToken>())
            .Returns(_ => CompleteUnpairAsync());

        var coordinator = new PairedDeviceCoordinator(storeWriter);
        var viewModel = new PairedDeviceListViewModel(pairedDeviceStore, coordinator, UiThreadDispatcher);
        viewModel.LoadedCommand.Execute(null);
        Assert.Single(viewModel.Items);

        viewModel.Items[0].UndoableDeleteCommand.Execute(null);
        Assert.Empty(viewModel.Items);

        var entry = viewModel.PendingDeletes[0];
        var finalizeTask = entry.FinalizeDeleteCommand.ExecuteAsync(null);
        Assert.Empty(viewModel.Items);

        deleteTcs.SetResult();
        await finalizeTask;

        Assert.Empty(viewModel.Items);

        async Task<StoreDeleteResult<PairedDeviceSnapshot>> CompleteUnpairAsync()
        {
            await deleteTcs.Task;
            pairedDeviceCache.RemoveKey(snapshot.DeviceId);
            return new StoreDeleteResult<PairedDeviceSnapshot>.Deleted(snapshot);
        }
    }

    [Fact]
    public void Items_UpdateOnlyWhileLoaded_AndReloadCurrentStoreState()
    {
        using var pairedDeviceCache = new SourceCache<PairedDeviceSnapshot, string>(snapshot => snapshot.DeviceId);
        var pairedDeviceStore = Substitute.For<IPairedDeviceStore>();
        pairedDeviceStore.Connect().Returns(pairedDeviceCache.Connect());
        pairedDeviceCache.AddOrUpdate(new PairedDeviceSnapshot(
            DeviceId: "device-1",
            DisplayName: "Phone",
            Expires: new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc)));

        var viewModel = new PairedDeviceListViewModel(
            pairedDeviceStore,
            Substitute.For<IPairedDeviceCoordinator>(),
            UiThreadDispatcher);

        Assert.Empty(viewModel.Items);

        viewModel.LoadedCommand.Execute(null);
        Assert.Single(viewModel.Items);

        viewModel.UnloadedCommand.Execute(null);
        pairedDeviceCache.AddOrUpdate(new PairedDeviceSnapshot(
            DeviceId: "device-2",
            DisplayName: "Tablet",
            Expires: new DateTime(2025, 1, 2, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Empty(viewModel.Items);

        viewModel.LoadedCommand.Execute(null);
        viewModel.LoadedCommand.Execute(null);
        Assert.Equal(2, viewModel.Items.Count);
    }

    [Fact]
    public async Task FinalizeUnpair_RestoresDevice_WhenCoordinatorReportsFailure()
    {
        var snapshot = new PairedDeviceSnapshot(
            DeviceId: "device-2",
            DisplayName: "Tablet",
            Expires: new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc));

        using var pairedDeviceCache = new SourceCache<PairedDeviceSnapshot, string>(s => s.DeviceId);
        pairedDeviceCache.AddOrUpdate(snapshot);

        var pairedDeviceStore = Substitute.For<IPairedDeviceStore>();
        pairedDeviceStore.Connect().Returns(pairedDeviceCache.Connect());
        pairedDeviceStore.Get(snapshot.DeviceId).Returns(snapshot);

        var storeWriter = Substitute.For<IPairedDeviceStoreWriter>();
        storeWriter.CommitLocalUnpairAsync(snapshot.DeviceId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoreDeleteResult<PairedDeviceSnapshot>>(
                new StoreDeleteResult<PairedDeviceSnapshot>.Failed("boom")));

        var coordinator = new PairedDeviceCoordinator(storeWriter);
        var viewModel = new PairedDeviceListViewModel(pairedDeviceStore, coordinator, UiThreadDispatcher);
        viewModel.LoadedCommand.Execute(null);
        Assert.Single(viewModel.Items);

        viewModel.Items[0].UndoableDeleteCommand.Execute(null);
        var entry = viewModel.PendingDeletes[0];
        await entry.FinalizeDeleteCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Items);
        Assert.Contains(viewModel.ErrorMessages, message => message.Contains("boom", StringComparison.Ordinal));
    }
}
