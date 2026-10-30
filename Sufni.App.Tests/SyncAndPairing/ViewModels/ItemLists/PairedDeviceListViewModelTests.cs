using Microsoft.Extensions.Time.Testing;
using DynamicData;
using NSubstitute;

using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.SyncAndPairing.ViewModels.ItemLists;
using Sufni.App.Shared.Models;
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

        var coordinator = Substitute.For<IPairedDeviceCoordinator>();
        var deleteTcs = new TaskCompletionSource();
        coordinator.UnpairAsync(snapshot.DeviceId)
            .Returns(_ => CompleteUnpairAsync());

        var viewModel = new PairedDeviceListViewModel(pairedDeviceStore, UiThreadDispatcher, pendingDeletes: TestPendingDeletes.Create(pairedDevices: coordinator), timeProvider: new FakeTimeProvider());
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

        async Task<PairedDeviceUnpairResult> CompleteUnpairAsync()
        {
            await deleteTcs.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
            pairedDeviceCache.RemoveKey(snapshot.DeviceId);
            return new PairedDeviceUnpairResult.Unpaired();
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
            UiThreadDispatcher,
            pendingDeletes: TestPendingDeletes.Create(pairedDevices: Substitute.For<IPairedDeviceCoordinator>()), timeProvider: new FakeTimeProvider());

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

        var coordinator = Substitute.For<IPairedDeviceCoordinator>();
        coordinator.UnpairAsync(snapshot.DeviceId)
            .Returns(Task.FromResult<PairedDeviceUnpairResult>(
                new PairedDeviceUnpairResult.Failed(PairedDeviceUnpairFailureReason.Unpair, "boom")));

        var viewModel = new PairedDeviceListViewModel(pairedDeviceStore, UiThreadDispatcher, pendingDeletes: TestPendingDeletes.Create(pairedDevices: coordinator), timeProvider: new FakeTimeProvider());
        viewModel.LoadedCommand.Execute(null);
        Assert.Single(viewModel.Items);

        viewModel.Items[0].UndoableDeleteCommand.Execute(null);
        var entry = viewModel.PendingDeletes[0];
        await entry.FinalizeDeleteCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Items);
        Assert.NotEmpty(viewModel.ErrorMessages);
    }
}
