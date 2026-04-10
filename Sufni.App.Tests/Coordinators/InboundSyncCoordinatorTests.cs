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
        // Setup handlers call GetAllAsync<Board>() on every arrival, so
        // always stub it to avoid a null-deref when no boards were seeded.
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
        var data = new SynchronizationData
        {
            Bikes = { new Bike(bikeId, "test bike") { HeadAngle = 65, ForkStroke = 160, Updated = 4 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        bikeStore.Received(1).Upsert(Arg.Is<BikeSnapshot>(s => s.Id == bikeId && s.Name == "test bike"));
        bikeStore.DidNotReceiveWithAnyArgs().Remove(default);
    }

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_RemovesDeletedBikes()
    {
        var coordinator = CreateCoordinator();
        var bikeId = Guid.NewGuid();
        var data = new SynchronizationData
        {
            Bikes = { new Bike(bikeId, "gone") { Updated = 5, Deleted = 5 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        bikeStore.Received(1).Remove(bikeId);
        bikeStore.DidNotReceiveWithAnyArgs().Upsert(default!);
    }

    // ----- Setups with matching and non-matching boards -----

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_UpsertsNonDeletedSetup_WithMatchingBoardId()
    {
        var setupId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
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
            s.Id == setupId && s.Name == "tuned" && s.BoardId == boardId));
    }

    [AvaloniaFact]
    public async Task SynchronizationDataArrived_UpsertsSetup_WithNullBoardId_WhenNoMatchingBoard()
    {
        var setupId = Guid.NewGuid();
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
        var data = new SynchronizationData
        {
            Bikes = { new Bike(bikeId, "test bike") { Updated = 1 } },
            Setups = { new Setup(setupId, "test setup") { BikeId = bikeId, Updated = 1 } },
        };

        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await DrainDispatcherAsync();

        bikeStore.Received(1).Upsert(Arg.Is<BikeSnapshot>(s => s.Id == bikeId));
        setupStore.Received(1).Upsert(Arg.Is<SetupSnapshot>(s => s.Id == setupId));
    }
}
