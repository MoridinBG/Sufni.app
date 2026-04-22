using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;

namespace Sufni.App.Tests.Coordinators;

public class InboundSyncCoordinatorTests
{
    private readonly IDatabaseService database = Substitute.For<IDatabaseService>();
    private readonly IBikeStoreWriter bikeStore = Substitute.For<IBikeStoreWriter>();
    private readonly ISetupStoreWriter setupStore = Substitute.For<ISetupStoreWriter>();
    private readonly ISynchronizationServerService server = Substitute.For<ISynchronizationServerService>();

    private InboundSyncCoordinator CreateCoordinator(List<Board>? boards = null)
    {
        // Bike/setup handlers reload authoritative state on every arrival,
        // so always seed board lookups to avoid null task results.
        database.GetAllAsync<Board>().Returns(Task.FromResult(boards ?? new List<Board>()));
        return new InboundSyncCoordinator(database, bikeStore, setupStore, server);
    }

    private static async Task DrainDispatcherAsync() =>
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    // ----- Bikes -----

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_UpsertsNonDeletedBikes()
    {
        var coordinator = CreateCoordinator();
        var bikeId = Guid.NewGuid();
        database.GetAsync<Bike>(bikeId).Returns(Task.FromResult(new Bike(bikeId, "fresh bike") { HeadAngle = 65, ForkStroke = 160, Updated = 7 }));
        var data = new SynchronizationData
        {
            Bikes = { new Bike(bikeId, "test bike") { HeadAngle = 65, ForkStroke = 160, Updated = 4 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        bikeStore.Received(1).Upsert(Arg.Is<BikeSnapshot>(s => s.Id == bikeId && s.Name == "fresh bike" && s.Updated == 7));
        bikeStore.DidNotReceiveWithAnyArgs().Remove(default);
    }

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_RemovesDeletedBikes()
    {
        var coordinator = CreateCoordinator();
        var bikeId = Guid.NewGuid();
        database.GetAsync<Bike>(bikeId).Returns(Task.FromResult<Bike>(null!));
        var data = new SynchronizationData
        {
            Bikes = { new Bike(bikeId, "gone") { Updated = 5, Deleted = 5 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        bikeStore.Received(1).Remove(bikeId);
        bikeStore.DidNotReceiveWithAnyArgs().Upsert(default!);
    }

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_DoesNotRemoveBike_WhenDeleteWasDiscardedByMerge()
    {
        var coordinator = CreateCoordinator();
        var bikeId = Guid.NewGuid();
        database.GetAsync<Bike>(bikeId).Returns(Task.FromResult(new Bike(bikeId, "kept bike") { Updated = 9 }));
        var data = new SynchronizationData
        {
            Bikes = { new Bike(bikeId, "gone") { Updated = 5, Deleted = 5 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        bikeStore.Received(1).Upsert(Arg.Is<BikeSnapshot>(s => s.Id == bikeId && s.Name == "kept bike"));
        bikeStore.DidNotReceive().Remove(bikeId);
    }

    // ----- Setups with matching and non-matching boards -----

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_UpsertsNonDeletedSetup_WithMatchingBoardId()
    {
        var setupId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        database.GetAsync<Setup>(setupId).Returns(Task.FromResult(new Setup(setupId, "fresh tuned") { BikeId = Guid.NewGuid(), Updated = 8 }));
        var coordinator = CreateCoordinator(boards: new List<Board>
        {
            new(boardId, setupId),
        });

        var data = new SynchronizationData
        {
            Setups = { new Setup(setupId, "tuned") { BikeId = Guid.NewGuid(), Updated = 3 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        setupStore.Received(1).Upsert(Arg.Is<SetupSnapshot>(s =>
            s.Id == setupId && s.Name == "fresh tuned" && s.BoardId == boardId && s.Updated == 8));
    }

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_UpsertsSetup_WithNullBoardId_WhenNoMatchingBoard()
    {
        var setupId = Guid.NewGuid();
        database.GetAsync<Setup>(setupId).Returns(Task.FromResult(new Setup(setupId, "untuned") { BikeId = Guid.NewGuid(), Updated = 3 }));
        var coordinator = CreateCoordinator(boards: new List<Board>
        {
            new(Guid.NewGuid(), Guid.NewGuid()),
        });

        var data = new SynchronizationData
        {
            Setups = { new Setup(setupId, "untuned") { BikeId = Guid.NewGuid(), Updated = 3 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        setupStore.Received(1).Upsert(Arg.Is<SetupSnapshot>(s =>
            s.Id == setupId && s.BoardId == null));
    }

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_RemovesDeletedSetups()
    {
        var coordinator = CreateCoordinator();
        var setupId = Guid.NewGuid();
        database.GetAsync<Setup>(setupId).Returns(Task.FromResult<Setup>(null!));
        var data = new SynchronizationData
        {
            Setups = { new Setup(setupId, "gone") { Updated = 9, Deleted = 9 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        setupStore.Received(1).Remove(setupId);
        setupStore.DidNotReceiveWithAnyArgs().Upsert(default!);
    }

    // ----- Mixed payload -----

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_MixedPayload_UpdatesBothStores()
    {
        var coordinator = CreateCoordinator();
        var bikeId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        database.GetAsync<Bike>(bikeId).Returns(Task.FromResult(new Bike(bikeId, "authoritative bike") { Updated = 2 }));
        database.GetAsync<Setup>(setupId).Returns(Task.FromResult(new Setup(setupId, "authoritative setup") { BikeId = bikeId, Updated = 2 }));
        var data = new SynchronizationData
        {
            Bikes = { new Bike(bikeId, "test bike") { Updated = 1 } },
            Setups = { new Setup(setupId, "test setup") { BikeId = bikeId, Updated = 1 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        bikeStore.Received(1).Upsert(Arg.Is<BikeSnapshot>(s => s.Id == bikeId && s.Name == "authoritative bike"));
        setupStore.Received(1).Upsert(Arg.Is<SetupSnapshot>(s => s.Id == setupId && s.Name == "authoritative setup"));
    }
}
