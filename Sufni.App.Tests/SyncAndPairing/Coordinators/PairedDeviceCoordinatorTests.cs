using System.Reactive.Linq;
using NSubstitute;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.SyncAndPairing.Coordinators;

public class PairedDeviceCoordinatorTests
{
    private static PairedDevice CreateDevice() => new("device-123", "Phone", new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnpairAsync_RemovesPersistedAndPublishedDevice_OnlyWhenPersistenceSucceeds(bool fail)
    {
        var device = CreateDevice();
        var repository = new InMemoryPairedDeviceRepository(device);
        if (fail) repository.DeleteFailure = new IOException("database unavailable");
        using var store = new PairedDeviceStore(new StorePublication(new InlineUiThreadDispatcher()));
        await store.ReplaceAllAsync([PairedDeviceSnapshot.From(device)]);
        var coordinator = new PairedDeviceCoordinator(store, repository, new InlineBackgroundTaskRunner());

        var result = await coordinator.UnpairAsync(device.DeviceId);

        if (fail)
        {
            Assert.Equal(PairedDeviceUnpairFailureReason.Unpair, Assert.IsType<PairedDeviceUnpairResult.Failed>(result).Reason);
            Assert.NotNull(await repository.GetPairedDeviceAsync(device.DeviceId));
            Assert.NotNull(store.Get(device.DeviceId));
        }
        else
        {
            Assert.IsType<PairedDeviceUnpairResult.Unpaired>(result);
            Assert.Null(await repository.GetPairedDeviceAsync(device.DeviceId));
            Assert.Null(store.Get(device.DeviceId));
        }
    }

}
