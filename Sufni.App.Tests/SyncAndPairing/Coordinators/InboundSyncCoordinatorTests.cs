using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NSubstitute;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Tests.SyncAndPairing.Coordinators;

[Collection("Ui")]
public class InboundSyncCoordinatorTests
{
    private readonly ISynchronizableRepository<Bike> bikeRepository = Substitute.For<ISynchronizableRepository<Bike>>();
    private readonly ISynchronizableRepository<Setup> setupRepository = Substitute.For<ISynchronizableRepository<Setup>>();
    private readonly IBikeStoreWriter bikeStore = Substitute.For<IBikeStoreWriter>();
    private readonly ISetupStoreWriter setupStore = Substitute.For<ISetupStoreWriter>();
    private readonly ISynchronizationServerService server = Substitute.For<ISynchronizationServerService>();

    private InboundSyncCoordinator CreateCoordinator() =>
        new(bikeRepository, setupRepository, bikeStore, setupStore, server);

    private static async Task DrainDispatcherAsync() =>
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    // ----- Bikes -----

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_UpsertsNonDeletedBikes()
    {
        var coordinator = CreateCoordinator();
        var bikeId = Guid.NewGuid();
        bikeRepository.GetAsync(bikeId).Returns(Task.FromResult<Bike?>(new Bike(bikeId, "fresh bike") { HeadAngle = 65, ForkStroke = 160, Updated = 7 }));
        var data = new SynchronizationData
        {
            Bikes = { new Bike(bikeId, "test bike") { HeadAngle = 65, ForkStroke = 160, Updated = 4 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        await bikeStore.Received(1).PublishBikesChangedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(bikeId)),
            Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_RemovesDeletedBikes()
    {
        var coordinator = CreateCoordinator();
        var bikeId = Guid.NewGuid();
        bikeRepository.GetAsync(bikeId).Returns(Task.FromResult<Bike?>(null));
        var data = new SynchronizationData
        {
            Bikes = { new Bike(bikeId, "gone") { Updated = 5, Deleted = 5 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        await bikeStore.Received(1).PublishBikesRemovedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(bikeId)),
            Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_DoesNotRemoveBike_WhenDeleteWasDiscardedByMerge()
    {
        var coordinator = CreateCoordinator();
        var bikeId = Guid.NewGuid();
        bikeRepository.GetAsync(bikeId).Returns(Task.FromResult<Bike?>(new Bike(bikeId, "kept bike") { Updated = 9 }));
        var data = new SynchronizationData
        {
            Bikes = { new Bike(bikeId, "gone") { Updated = 5, Deleted = 5 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        await bikeStore.Received(1).PublishBikesChangedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(bikeId)),
            Arg.Any<CancellationToken>());
    }

    // ----- Setups -----

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_PublishesNonDeletedSetupChange()
    {
        var setupId = Guid.NewGuid();
        setupRepository.GetAsync(setupId).Returns(Task.FromResult<Setup?>(new Setup(setupId, "fresh tuned") { BikeId = Guid.NewGuid(), Updated = 8 }));
        var coordinator = CreateCoordinator();

        var data = new SynchronizationData
        {
            Setups = { new Setup(setupId, "tuned") { BikeId = Guid.NewGuid(), Updated = 3 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        await setupStore.Received(1).PublishSetupsChangedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(setupId)),
            Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_PublishesSetupChange_WhenAuthoritativeSetupExists()
    {
        var setupId = Guid.NewGuid();
        setupRepository.GetAsync(setupId).Returns(Task.FromResult<Setup?>(new Setup(setupId, "untuned") { BikeId = Guid.NewGuid(), Updated = 3 }));
        var coordinator = CreateCoordinator();

        var data = new SynchronizationData
        {
            Setups = { new Setup(setupId, "untuned") { BikeId = Guid.NewGuid(), Updated = 3 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        await setupStore.Received(1).PublishSetupsChangedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(setupId)),
            Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_RemovesDeletedSetups()
    {
        var coordinator = CreateCoordinator();
        var setupId = Guid.NewGuid();
        setupRepository.GetAsync(setupId).Returns(Task.FromResult<Setup?>(null));
        var data = new SynchronizationData
        {
            Setups = { new Setup(setupId, "gone") { Updated = 9, Deleted = 9 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        await setupStore.Received(1).PublishSetupsRemovedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(setupId)),
            Arg.Any<CancellationToken>());
        await setupStore.DidNotReceive().PublishSetupsChangedAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
    }

    // ----- Mixed payload -----

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_MixedPayload_UpdatesBothStores()
    {
        var coordinator = CreateCoordinator();
        var bikeId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        bikeRepository.GetAsync(bikeId).Returns(Task.FromResult<Bike?>(new Bike(bikeId, "authoritative bike") { Updated = 2 }));
        setupRepository.GetAsync(setupId).Returns(Task.FromResult<Setup?>(new Setup(setupId, "authoritative setup") { BikeId = bikeId, Updated = 2 }));
        var data = new SynchronizationData
        {
            Bikes = { new Bike(bikeId, "test bike") { Updated = 1 } },
            Setups = { new Setup(setupId, "test setup") { BikeId = bikeId, Updated = 1 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        await bikeStore.Received(1).PublishBikesChangedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(bikeId)),
            Arg.Any<CancellationToken>());
        await setupStore.Received(1).PublishSetupsChangedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(setupId)),
            Arg.Any<CancellationToken>());
    }
}
