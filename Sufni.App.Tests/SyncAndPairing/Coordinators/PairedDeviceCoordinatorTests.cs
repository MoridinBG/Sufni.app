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
        using var coordinator = new PairedDeviceCoordinator(store, repository, new InlineBackgroundTaskRunner());

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

    [Fact]
    public async Task ServerPairingAndUnpairing_PublishTheDeviceState()
    {
        var device = CreateDevice();
        var server = Substitute.For<ISynchronizationServerService>();
        using var store = new PairedDeviceStore(new StorePublication(new InlineUiThreadDispatcher()));
        using var coordinator = new PairedDeviceCoordinator(store, new InMemoryPairedDeviceRepository(), new InlineBackgroundTaskRunner(), server);
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var changes = store.Connect().Subscribe(_ => published.TrySetResult());

        server.PairingConfirmed += Raise.EventWith(server, new PairingEventArgs(device));
        await published.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PairedDeviceSnapshot.From(device), store.Get(device.DeviceId));

        published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Unpaired += Raise.EventWith(server, new PairingEventArgs(device));
        await published.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.Null(store.Get(device.DeviceId));
    }
}
