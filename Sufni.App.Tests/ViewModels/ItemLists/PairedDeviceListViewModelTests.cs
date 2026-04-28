using DynamicData;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels.ItemLists;

namespace Sufni.App.Tests.ViewModels.ItemLists;

public class PairedDeviceListViewModelTests
{
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
        storeWriter.When(w => w.Remove(Arg.Any<string>()))
            .Do(call => pairedDeviceCache.RemoveKey(call.Arg<string>()));

        var dbService = Substitute.For<IDatabaseService>();
        var deleteTcs = new TaskCompletionSource();
        dbService.DeletePairedDeviceAsync(snapshot.DeviceId).Returns(deleteTcs.Task);

        var coordinator = new PairedDeviceCoordinator(storeWriter, dbService);
        var viewModel = new PairedDeviceListViewModel(pairedDeviceStore, coordinator);
        Assert.Single(viewModel.Items);

        viewModel.Items[0].UndoableDeleteCommand.Execute(null);
        Assert.Empty(viewModel.Items);

        var finalizeTask = viewModel.FinalizeDeleteCommand.ExecuteAsync(null);
        Assert.Empty(viewModel.Items);

        deleteTcs.SetResult();
        await finalizeTask;

        Assert.Empty(viewModel.Items);
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

        var dbService = Substitute.For<IDatabaseService>();
        dbService.DeletePairedDeviceAsync(snapshot.DeviceId)
            .Returns(Task.FromException(new InvalidOperationException("boom")));

        var coordinator = new PairedDeviceCoordinator(storeWriter, dbService);
        var viewModel = new PairedDeviceListViewModel(pairedDeviceStore, coordinator);
        Assert.Single(viewModel.Items);

        viewModel.Items[0].UndoableDeleteCommand.Execute(null);
        await viewModel.FinalizeDeleteCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Items);
        Assert.Contains(viewModel.ErrorMessages, message => message.Contains("boom", StringComparison.Ordinal));
    }
}
