using Sufni.App.LiveDaq.Queries;
using Sufni.App.Bikes.Services;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.SyncAndPairing.Stores;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.Stores;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.SyncAndPairing.Coordinators;

public class InboundSyncCoordinatorTests : IDisposable
{
    private readonly InMemorySynchronizableRepository<Bike> bikes;
    private readonly InMemorySynchronizableRepository<Setup> setups;
    private readonly InMemorySynchronizableRepository<Board> boards;
    private readonly StorePublication publication = new(new InlineUiThreadDispatcher());
    private readonly BoardStore boardStore;
    private readonly BikeStore bikeStore;
    private readonly SetupStore setupStore;
    private readonly SessionSegmentStore segmentStore;
    private readonly ISessionSegmentRepository segmentRepository = Substitute.For<ISessionSegmentRepository>();
    private readonly ISynchronizationServerService server = Substitute.For<ISynchronizationServerService>();

    public InboundSyncCoordinatorTests()
    {
        var clock = new FakeTimeProvider();
        bikes = new(clock);
        setups = new(clock);
        boards = new(clock);
        boardStore = new(publication);
        bikeStore = new(publication);
        setupStore = new(publication);
        segmentStore = new(publication);
        _ = new InboundSyncCoordinator(bikes, setups, bikeStore, setupStore, segmentStore, server,
            new InlineUiThreadDispatcher(), segmentRepository, new InlineBackgroundTaskRunner(), boards, boardStore, publication);
    }

    public void Dispose()
    {
        boardStore.Dispose();
        bikeStore.Dispose();
        setupStore.Dispose();
        segmentStore.Dispose();
        publication.Dispose();
    }

    private async Task PublishAsync(SynchronizationData data, object changedStore)
    {
        var published = publication.Committed.Where(commit => commit.Stores.Contains(changedStore))
            .FirstAsync().ToTask(TestContext.Current.CancellationToken);
        server.SynchronizationDataArrived += Raise.EventWith(server, new SynchronizationDataArrivedEventArgs(data));
        await published.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task BoardOnlyArrival_PublishesAuthoritativeBoardAndRefreshesKnownBoardQuery()
    {
        var setup = TestSnapshots.Setup(name: "arrived setup");
        await setupStore.PublishSetupsChangedAsync([setup]);
        var board = new Board(Guid.NewGuid(), setup.Id) { Updated = 7 };
        boards.Seed(board);
        using var query = new LiveDaqKnownBoardsQuery(boardStore, setupStore, bikeStore,
            new TelemetryBikeProcessingContextFactory(new RearTravelCalibrationBuilder(new KinematicSolutionCache())),
            new InlineBackgroundTaskRunner(), new InlineUiThreadDispatcher(), publication);
        Assert.Null(query.Get(board.Id.ToString()));

        await PublishAsync(new SynchronizationData { Boards = { new Board(board.Id, null) } }, boardStore);

        Assert.Equal(BoardSnapshot.From(board), boardStore.Get(board.Id));
        Assert.Equal("arrived setup", query.Get(board.Id.ToString())!.DisplayName);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SynchronizationDataArrived_PublishesAuthoritativeBikeState(bool incomingDeleted, bool authoritativeExists)
    {
        var id = Guid.NewGuid();
        var incoming = new Bike(id, "incoming") { Updated = 4, Deleted = incomingDeleted ? 5 : null };
        await bikeStore.ReplaceAllAsync([BikeSnapshot.From(new Bike(id, "old"))]);
        var authoritative = new Bike(id, "current") { Updated = 7, ForkStroke = 160 };
        if (authoritativeExists) bikes.Seed(authoritative);

        await PublishAsync(new SynchronizationData { Bikes = { incoming } }, bikeStore);

        Assert.Equal(authoritativeExists ? BikeSnapshot.From(authoritative) : null, bikeStore.Get(id));
    }

    [Theory]
    [InlineData(false, true, 8)]
    [InlineData(false, true, 3)]
    [InlineData(true, false, 0)]
    public async Task SynchronizationDataArrived_PublishesAuthoritativeSetupAndBoard(bool incomingDeleted, bool authoritativeExists, int updated)
    {
        var id = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var incoming = new Setup(id, "incoming") { Updated = 3, Deleted = incomingDeleted ? 9 : null };
        await setupStore.ReplaceAllAsync([SetupSnapshot.From(new Setup(id, "old"), null)]);
        var authoritative = new Setup(id, "current") { Updated = updated, BikeId = Guid.NewGuid() };
        if (authoritativeExists) setups.Seed(authoritative);
        boards.Seed(new Board(boardId, id));

        await PublishAsync(new SynchronizationData { Setups = { incoming } }, setupStore);

        Assert.Equal(authoritativeExists ? SetupSnapshot.From(authoritative, boardId) : null, setupStore.Get(id));
    }

    [Fact]
    public async Task SynchronizationDataArrived_MixedPayload_PublishesBothAuthoritativeRows()
    {
        var bike = new Bike(Guid.NewGuid(), "current bike") { Updated = 2 };
        var setup = new Setup(Guid.NewGuid(), "current setup") { BikeId = bike.Id, Updated = 2 };
        bikes.Seed(bike);
        setups.Seed(setup);
        var data = new SynchronizationData
        {
            Bikes = { new Bike(bike.Id, "incoming bike") { Updated = 1 } },
            Setups = { new Setup(setup.Id, "incoming setup") { Updated = 1 } }
        };

        await PublishAsync(data, setupStore);

        Assert.Equal(BikeSnapshot.From(bike), bikeStore.Get(bike.Id));
        Assert.Equal(SetupSnapshot.From(setup, null), setupStore.Get(setup.Id));
    }

    [Fact]
    public async Task SynchronizationDataArrived_WithSegments_RefreshesAuthoritativeSegments()
    {
        var authoritative = new SessionSegment { Id = Guid.NewGuid(), OwnerKey = "source-hash", Name = "current", StartSeconds = 2, EndSeconds = 4 };
        segmentRepository.GetLiveAsync().Returns([authoritative]);
        var data = new SynchronizationData
        {
            Segments = { new SessionSegment { OwnerKey = authoritative.OwnerKey, Name = "incoming", StartSeconds = 1, EndSeconds = 2 } }
        };

        await PublishAsync(data, segmentStore);

        Assert.Equal(SessionSegmentSnapshot.From(authoritative), Assert.Single(segmentStore.GetForOwner(authoritative.OwnerKey)));
    }
}
