using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Stores;
using System.Linq;
using DynamicData;
using NSubstitute;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.LiveDaq.Queries;

public class LiveDaqKnownBoardsQueryTests : IDisposable
{
    private readonly StorePublication publication = new(new InlineUiThreadDispatcher());
    private readonly BoardStore boardStore;
    private readonly SetupStore setupStore;
    private readonly BikeStore bikeStore;

    public LiveDaqKnownBoardsQueryTests()
    {
        boardStore = new BoardStore(publication);
        setupStore = new SetupStore(publication);
        bikeStore = new BikeStore(publication);
    }

    private LiveDaqKnownBoardsQuery CreateQuery(IBackgroundTaskRunner? runner = null) => new(
        boardStore,
        setupStore,
        bikeStore,
        new TelemetryBikeProcessingContextFactory(
            new RearTravelCalibrationBuilder(
                new KinematicSolutionCache())), runner ?? new InlineBackgroundTaskRunner(), new InlineUiThreadDispatcher(), publication);

    [Fact]
    public async Task BoardOnlyReassignmentAndRemoval_UpdateKnownRowsWithoutASetupChange()
    {
        var first = TestSnapshots.Setup(name: "first");
        var second = TestSnapshots.Setup(name: "second");
        await setupStore.PublishSetupsChangedAsync([first, second]);
        var board = new BoardSnapshot(Guid.NewGuid(), first.Id, 1);
        await boardStore.PublishBoardsChangedAsync([board]);
        using var query = CreateQuery();
        Assert.Equal("first", query.Get(board.Id.ToString())!.DisplayName);

        await boardStore.PublishBoardsChangedAsync([board with { SetupId = second.Id, Updated = 2 }]);
        Assert.Equal("second", query.Get(board.Id.ToString())!.DisplayName);
        await boardStore.PublishBoardsRemovedAsync([board.Id]);
        Assert.Null(query.Get(board.Id.ToString()));
    }

    [Fact]
    public async Task CommittedBatch_PublishesOneCompleteBoardSetupBikeProjection()
    {
        using var query = CreateQuery();
        var observed = new List<IReadOnlyList<KnownLiveDaqRecord>>();
        using var subscription = query.Changes.Skip(1).Subscribe(observed.Add);
        var bike = TestSnapshots.Bike(name: "bike");
        var setup = TestSnapshots.Setup(name: "setup", bikeId: bike.Id);
        var board = new BoardSnapshot(Guid.NewGuid(), setup.Id, 1);
        using (publication.BeginBatch())
        {
            await boardStore.PublishBoardsChangedAsync([board]);
            await setupStore.PublishSetupsChangedAsync([setup]);
            await bikeStore.PublishBikesChangedAsync([bike]);
            Assert.Empty(observed);
        }

        var record = Assert.Single(Assert.Single(observed));
        Assert.Equal((setup.Id, bike.Id, "setup"), (record.SetupId, record.BikeId, record.DisplayName));
    }

    [Fact]
    public async Task PendingProjection_IsSupersededByTheLatestBoardFacts()
    {
        var board = new BoardSnapshot(Guid.NewGuid(), null, 1);
        await boardStore.PublishBoardsChangedAsync([board]);
        var runner = new DeferredBackgroundTaskRunner();
        using var query = CreateQuery(runner);
        var setup = TestSnapshots.Setup(name: "latest");
        using (publication.BeginBatch())
        {
            await setupStore.PublishSetupsChangedAsync([setup]);
            await boardStore.PublishBoardsChangedAsync([board with { SetupId = setup.Id, Updated = 2 }]);
        }
        var next = query.Changes.Where(records => records.Count > 0).FirstAsync().ToTask(TestContext.Current.CancellationToken);

        await runner.RunNextAsync();
        await runner.RunNextAsync();
        var record = Assert.Single(await next.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("latest", record.DisplayName);
    }

    [Fact]
    public async Task Changes_ReturnsBoardOnlyRecord_WhenBoardHasNoSetup()
    {
        var boardId = Guid.NewGuid();
        await boardStore.PublishBoardsChangedAsync([new BoardSnapshot(boardId, null, 1)]);

        using var query = CreateQuery();
        var records = await WaitForRecordsAsync(query.Changes);

        var record = Assert.Single(records);
        Assert.Equal(boardId.ToString(), record.IdentityKey);
        Assert.Equal(boardId.ToString(), record.DisplayName);
        Assert.Equal(boardId.ToString(), record.BoardId);
        Assert.Null(record.SetupId);
        Assert.Null(record.SetupName);
        Assert.Null(record.BikeId);
        Assert.Null(record.BikeName);
    }

    [Fact]
    public async Task Changes_UsesSetupNameAsDisplayName_WhenSetupIsPresent()
    {
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "enduro setup", bikeId: Guid.NewGuid());
        var bike = TestSnapshots.Bike(id: setup.BikeId, name: "enduro bike");

        await SeedSetupAsync(setup);
        await SeedBikeAsync(bike);
        await boardStore.PublishBoardsChangedAsync([new BoardSnapshot(boardId, setup.Id, 1)]);

        using var query = CreateQuery();
        var records = await WaitForRecordsAsync(query.Changes);

        var record = Assert.Single(records);
        Assert.Equal("enduro setup", record.DisplayName);
    }

    [Fact]
    public async Task Changes_ReturnsEnrichedRecord_WhenBoardMapsToSetupAndBike()
    {
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "park setup", bikeId: Guid.NewGuid());
        var bike = TestSnapshots.Bike(id: setup.BikeId, name: "demo bike");

        await SeedSetupAsync(setup);
        await SeedBikeAsync(bike);
        await boardStore.PublishBoardsChangedAsync([new BoardSnapshot(boardId, setup.Id, 1)]);

        using var query = CreateQuery();
        var records = await WaitForRecordsAsync(query.Changes);

        var record = Assert.Single(records);
        Assert.Equal(boardId.ToString(), record.BoardId);
        Assert.Equal(setup.Id, record.SetupId);
        Assert.Equal("park setup", record.SetupName);
        Assert.Equal(bike.Id, record.BikeId);
        Assert.Equal("demo bike", record.BikeName);
    }

    [Fact]
    public async Task Changes_RebuildsProjection_WhenSetupStoreFirstLoadsAfterQueryStarts()
    {
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "startup setup", bikeId: Guid.NewGuid());
        await boardStore.PublishBoardsChangedAsync([new BoardSnapshot(boardId, setup.Id, 1)]);

        using var query = CreateQuery();
        var initialRecords = await WaitForRecordsAsync(query.Changes);
        var initialRecord = Assert.Single(initialRecords);
        Assert.Equal(boardId.ToString(), initialRecord.DisplayName);
        Assert.Null(initialRecord.SetupId);
        Assert.Null(initialRecord.SetupName);

        var nextChange = WaitForRecordsAsync(query.Changes, ignoreReplay: true);
        await SeedSetupAsync(setup);
        var updatedRecords = await nextChange;

        var updatedRecord = Assert.Single(updatedRecords);
        Assert.Equal(setup.Id, updatedRecord.SetupId);
        Assert.Equal("startup setup", updatedRecord.SetupName);
        Assert.Equal("startup setup", updatedRecord.DisplayName);
    }

    [Fact]
    public async Task Changes_RebuildsProjection_WhenBikeStoreChanges()
    {
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "trail setup", bikeId: Guid.NewGuid());
        var bike = TestSnapshots.Bike(id: setup.BikeId, name: "old bike name");

        await SeedSetupAsync(setup);
        await SeedBikeAsync(bike);
        await boardStore.PublishBoardsChangedAsync([new BoardSnapshot(boardId, setup.Id, 1)]);

        using var query = CreateQuery();
        await WaitForRecordsAsync(query.Changes);

        var nextChange = WaitForRecordsAsync(query.Changes, ignoreReplay: true);
        await SeedBikeAsync(bike with { Name = "new bike name" });
        var records = await nextChange;

        var record = Assert.Single(records);
        Assert.Equal("new bike name", record.BikeName);
    }

    [Fact]
    public async Task Get_ReturnsLatestRecord_ByIdentityKey()
    {
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "known setup", bikeId: Guid.NewGuid());
        var bike = TestSnapshots.Bike(id: setup.BikeId, name: "known bike");

        await SeedSetupAsync(setup);
        await SeedBikeAsync(bike);
        await boardStore.PublishBoardsChangedAsync([new BoardSnapshot(boardId, setup.Id, 1)]);

        using var query = CreateQuery();
        await WaitForRecordsAsync(query.Changes);

        var record = query.Get(boardId.ToString());

        Assert.NotNull(record);
        Assert.Equal("known setup", record!.SetupName);
        Assert.Equal("known bike", record.BikeName);
    }

    [Fact]
    public async Task GetTravelCalibration_ReturnsCurrentCalibration_WhenSetupAndBikeAreKnown()
    {
        var boardId = Guid.NewGuid();
        var bike = TestSnapshots.Bike(id: Guid.NewGuid(), name: "calibrated bike");
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "calibrated setup", bikeId: bike.Id) with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 8,
                Resolution = 10,
            })
        };

        await SeedSetupAsync(setup);
        await SeedBikeAsync(bike);
        await boardStore.PublishBoardsChangedAsync([new BoardSnapshot(boardId, setup.Id, 1)]);

        using var query = CreateQuery();
        await WaitForRecordsAsync(query.Changes);

        var calibration = query.GetTravelCalibration(boardId.ToString());

        Assert.NotNull(calibration);
        Assert.NotNull(calibration!.Front);
        Assert.Null(calibration.Rear);
        var expectedFrontMaxTravel = bike.ForkStroke!.Value * Math.Sin(bike.HeadAngle * Math.PI / 180.0);
        var expectedFrontMeasurementToTravel = 8 / (Math.Pow(2.0, 10) - 1.0) * Math.Sin(bike.HeadAngle * Math.PI / 180.0);
        Assert.Equal(expectedFrontMaxTravel, calibration.Front!.MaxTravel, 6);
        Assert.Equal(expectedFrontMeasurementToTravel, calibration.Front.MeasurementToTravel(1), 9);
    }

    [Fact]
    public async Task GetTravelCalibration_ReturnsCachedInstance_BetweenCalls()
    {
        var boardId = Guid.NewGuid();
        var bike = TestSnapshots.Bike(id: Guid.NewGuid(), name: "cached calibration bike");
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "cached calibration setup", bikeId: bike.Id) with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 8,
                Resolution = 10,
            })
        };

        await SeedSetupAsync(setup);
        await SeedBikeAsync(bike);
        await boardStore.PublishBoardsChangedAsync([new BoardSnapshot(boardId, setup.Id, 1)]);

        using var query = CreateQuery();
        await WaitForRecordsAsync(query.Changes);

        var first = query.GetTravelCalibration(boardId.ToString());
        var second = query.GetTravelCalibration(boardId.ToString());

        Assert.Same(first, second);
        Assert.Same(first!.Front, second!.Front);
    }

    [Fact]
    public async Task GetSessionContext_ReturnsBikeDataAndCalibration_WhenSetupAndBikeAreKnown()
    {
        var boardId = Guid.NewGuid();
        var bike = TestSnapshots.Bike(id: Guid.NewGuid(), name: "session bike") with
        {
            HeadAngle = 63.5,
            ForkStroke = 170,
            ShockStroke = 0.5,
            RearSuspension = new RearSuspensionSpec.Linkage(TestSnapshots.FullSuspensionLinkageSpec()),
        };
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "session setup", bikeId: bike.Id) with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 120,
                Resolution = 12,
            }),
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = 55,
                Resolution = 12,
            }),
        };

        await SeedSetupAsync(setup);
        await SeedBikeAsync(bike);
        await boardStore.PublishBoardsChangedAsync([new BoardSnapshot(boardId, setup.Id, 1)]);

        using var query = CreateQuery();
        await WaitForRecordsAsync(query.Changes);

        var context = query.GetSessionContext(boardId.ToString());

        Assert.NotNull(context);
        Assert.Equal(boardId, context!.BoardId);
        Assert.Equal(setup.Id, context.SetupId);
        Assert.Equal("session setup", context.SetupName);
        Assert.Equal(bike.Id, context.BikeId);
        Assert.Equal("session bike", context.BikeName);
        Assert.NotNull(context.BikeData.FrontMeasurementToTravel);
        Assert.NotNull(context.BikeData.RearMeasurementToTravel);
        Assert.NotNull(context.TravelCalibration.Front);
        Assert.NotNull(context.TravelCalibration.Rear);
        Assert.Same(context.BikeData.FrontMeasurementToTravel, context.TravelCalibration.Front!.MeasurementToTravel);
        Assert.Same(context.BikeData.RearMeasurementToTravel, context.TravelCalibration.Rear!.MeasurementToTravel);
    }

    [Fact]
    public async Task GetSessionContext_ReturnsCachedInstance_BetweenCalls()
    {
        var boardId = Guid.NewGuid();
        var bike = TestSnapshots.Bike(id: Guid.NewGuid(), name: "cached session bike") with
        {
            HeadAngle = 63.5,
            ForkStroke = 170,
            ShockStroke = 0.5,
            RearSuspension = new RearSuspensionSpec.Linkage(TestSnapshots.FullSuspensionLinkageSpec()),
        };
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "cached session setup", bikeId: bike.Id) with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 120,
                Resolution = 12,
            }),
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = 55,
                Resolution = 12,
            }),
        };

        await SeedSetupAsync(setup);
        await SeedBikeAsync(bike);
        await boardStore.PublishBoardsChangedAsync([new BoardSnapshot(boardId, setup.Id, 1)]);

        using var query = CreateQuery();
        await WaitForRecordsAsync(query.Changes);

        var first = query.GetSessionContext(boardId.ToString());
        var second = query.GetSessionContext(boardId.ToString());

        Assert.Same(first, second);
        Assert.Same(first!.TravelCalibration, second!.TravelCalibration);
    }

    [Fact]
    public async Task GetSessionContext_ComposesRearTravel_ForLeverageRatioBike()
    {
        var boardId = Guid.NewGuid();
        var bike = TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)),
            shockStroke: 20,
            id: Guid.NewGuid(),
            name: "curve bike");
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "curve setup", bikeId: bike.Id) with
        {
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = 24,
                Resolution = 4,
                Type = SensorType.LinearShockStroke,
            })
        };

        await SeedSetupAsync(setup);
        await SeedBikeAsync(bike);
        await boardStore.PublishBoardsChangedAsync([new BoardSnapshot(boardId, setup.Id, 1)]);

        using var query = CreateQuery();
        await WaitForRecordsAsync(query.Changes);

        var context = query.GetSessionContext(boardId.ToString());

        Assert.NotNull(context);
        Assert.Null(context!.TravelCalibration.Front);
        Assert.NotNull(context.TravelCalibration.Rear);
        Assert.NotNull(context.BikeData.RearMeasurementToTravel);
        Assert.Equal(12, context.TravelCalibration.Rear!.MeasurementToTravel(3), 6);
        Assert.Equal(20, context.BikeData.RearMeasurementToTravel!((ushort)5), 6);
    }

    private async Task SeedBikeAsync(BikeSnapshot bike) =>
        await bikeStore.PublishBikesChangedAsync([bike]);

    private async Task SeedSetupAsync(SetupSnapshot setup)
    {
        using var batch = publication.BeginBatch();
        await setupStore.PublishSetupsChangedAsync([setup]);
    }

    public void Dispose()
    {
        boardStore.Dispose();
        setupStore.Dispose();
        bikeStore.Dispose();
        publication.Dispose();
    }

    private static async Task<IReadOnlyList<KnownLiveDaqRecord>> WaitForRecordsAsync(
        IObservable<IReadOnlyList<KnownLiveDaqRecord>> changes,
        bool ignoreReplay = false)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<KnownLiveDaqRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var seenReplay = !ignoreReplay;
        IDisposable? subscription = null;
        subscription = changes.Subscribe(records =>
        {
            if (!seenReplay)
            {
                seenReplay = true;
                return;
            }

            subscription?.Dispose();
            tcs.TrySetResult(records);
        });

        return await tcs.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
    }
}
