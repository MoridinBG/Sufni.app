using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;

namespace Sufni.App.Tests.Coordinators;

public class PairedDeviceCoordinatorTests
{
    private readonly IPairedDeviceStoreWriter pairedDeviceStore = Substitute.For<IPairedDeviceStoreWriter>();
    private readonly IDatabaseService database = Substitute.For<IDatabaseService>();

    private PairedDeviceCoordinator CreateCoordinator(ISynchronizationServerService? server = null) =>
        new(pairedDeviceStore, database, server);

    // ----- UnpairAsync -----

    [Fact]
    public async Task UnpairAsync_DeletesFromDatabase_RemovesFromStore_AndReturnsUnpaired()
    {
        var coordinator = CreateCoordinator();

        var result = await coordinator.UnpairAsync("device-123");

        Assert.IsType<PairedDeviceUnpairResult.Unpaired>(result);
        await database.Received(1).DeletePairedDeviceAsync("device-123");
        pairedDeviceStore.Received(1).Remove("device-123");
    }

    [Fact]
    public async Task UnpairAsync_ReturnsFailed_AndDoesNotRemoveFromStore_WhenDatabaseThrows()
    {
        database.DeletePairedDeviceAsync(Arg.Any<string>())
            .ThrowsAsync(new InvalidOperationException("boom"));
        var coordinator = CreateCoordinator();

        var result = await coordinator.UnpairAsync("device-123");

        Assert.IsType<PairedDeviceUnpairResult.Failed>(result);
        pairedDeviceStore.DidNotReceiveWithAnyArgs().Remove(default!);
    }

    // ----- Constructor tolerates null server -----

    [Fact]
    public async Task Constructor_WithNullServer_LeavesOnlyExplicitUnpairPathAvailable()
    {
        var coordinator = CreateCoordinator(server: null);

        var result = await coordinator.UnpairAsync("device-abc");
        Assert.IsType<PairedDeviceUnpairResult.Unpaired>(result);
        await database.Received(1).DeletePairedDeviceAsync("device-abc");
    }

    // ----- Server event subscriptions -----

    [AvaloniaFact]
    public async Task PairingConfirmed_UpsertsSnapshotOntoStore()
    {
        var server = Substitute.For<ISynchronizationServerService>();
        var coordinator = CreateCoordinator(server);

        var device = new PairedDevice("device-xyz", "My Phone", new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));
        server.PairingConfirmed += Raise.EventWith(server, new PairingEventArgs(device));

        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        pairedDeviceStore.Received(1).Upsert(Arg.Is<PairedDeviceSnapshot>(s =>
            s.DeviceId == "device-xyz" &&
            s.DisplayName == "My Phone" &&
            s.Expires == new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc)));
    }

    [AvaloniaFact]
    public async Task Unpaired_RemovesDeviceFromStore()
    {
        var server = Substitute.For<ISynchronizationServerService>();
        var coordinator = CreateCoordinator(server);

        var device = new PairedDevice("device-xyz", "My Phone", DateTime.UtcNow);
        server.Unpaired += Raise.EventWith(server, new PairingEventArgs(device));

        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        pairedDeviceStore.Received(1).Remove("device-xyz");
    }
}
