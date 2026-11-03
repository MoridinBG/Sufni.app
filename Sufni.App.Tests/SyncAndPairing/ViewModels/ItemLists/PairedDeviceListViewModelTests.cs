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

}
