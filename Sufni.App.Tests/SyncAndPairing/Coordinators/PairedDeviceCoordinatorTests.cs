using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Shared.Stores;
namespace Sufni.App.Tests.SyncAndPairing.Coordinators;

[Collection("Ui")]
public class PairedDeviceCoordinatorTests
{
    private readonly IPairedDeviceStoreWriter pairedDeviceStore = Substitute.For<IPairedDeviceStoreWriter>();

    private PairedDeviceCoordinator CreateCoordinator(ISynchronizationServerService? server = null) =>
        new(pairedDeviceStore, server);

    public PairedDeviceCoordinatorTests()
    {
        pairedDeviceStore.CommitLocalUnpairAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoreDeleteResult<PairedDeviceSnapshot>>(
                new StoreDeleteResult<PairedDeviceSnapshot>.Deleted()));
    }

    // ----- UnpairAsync -----

    [Fact]
    public async Task UnpairAsync_CommitsLocalUnpair_AndReturnsUnpaired()
    {
        var coordinator = CreateCoordinator();

        var result = await coordinator.UnpairAsync("device-123");

        Assert.IsType<PairedDeviceUnpairResult.Unpaired>(result);
        await pairedDeviceStore.Received(1).CommitLocalUnpairAsync("device-123", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnpairAsync_ReturnsFailed_AndDoesNotRemoveFromStore_WhenDatabaseThrows()
    {
        pairedDeviceStore.CommitLocalUnpairAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoreDeleteResult<PairedDeviceSnapshot>>(
                new StoreDeleteResult<PairedDeviceSnapshot>.Failed("boom")));
        var coordinator = CreateCoordinator();

        var result = await coordinator.UnpairAsync("device-123");

        Assert.IsType<PairedDeviceUnpairResult.Failed>(result);
        await pairedDeviceStore.Received(1).CommitLocalUnpairAsync("device-123", Arg.Any<CancellationToken>());
    }

    // ----- Constructor tolerates null server -----

    [Fact]
    public async Task Constructor_WithNullServer_LeavesOnlyExplicitUnpairPathAvailable()
    {
        var coordinator = CreateCoordinator(server: null);

        var result = await coordinator.UnpairAsync("device-abc");
        Assert.IsType<PairedDeviceUnpairResult.Unpaired>(result);
        await pairedDeviceStore.Received(1).CommitLocalUnpairAsync("device-abc", Arg.Any<CancellationToken>());
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

        await pairedDeviceStore.Received(1).PublishPairedDevicesChangedAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids => ids.Count == 1 && ids.Contains("device-xyz")),
            Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task Unpaired_RemovesDeviceFromStore()
    {
        var server = Substitute.For<ISynchronizationServerService>();
        var coordinator = CreateCoordinator(server);

        var device = new PairedDevice("device-xyz", "My Phone", DateTime.UtcNow);
        server.Unpaired += Raise.EventWith(server, new PairingEventArgs(device));

        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        await pairedDeviceStore.Received(1).PublishPairedDevicesRemovedAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids => ids.Count == 1 && ids.Contains("device-xyz")),
            Arg.Any<CancellationToken>());
    }
}
