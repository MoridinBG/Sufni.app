using System.Linq;
using DynamicData;
using NSubstitute;
using Sufni.App.Models;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Queries;

public class LiveDaqKnownBoardsQueryTests
{
    private readonly IDatabaseService database = Substitute.For<IDatabaseService>();
    private readonly TestSetupStore setupStore = new();
    private readonly TestBikeStore bikeStore = new();

    private LiveDaqKnownBoardsQuery CreateQuery() => new(database, setupStore, bikeStore);

    [Fact]
    public async Task Changes_ReturnsBoardOnlyRecord_WhenBoardHasNoSetup()
    {
        var boardId = Guid.NewGuid();
        database.GetAllAsync<Board>().Returns(Task.FromResult(new List<Board> { new(boardId, null) }));

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
    public async Task Changes_ReturnsEnrichedRecord_WhenBoardMapsToSetupAndBike()
    {
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "park setup", bikeId: Guid.NewGuid(), boardId: boardId);
        var bike = TestSnapshots.Bike(id: setup.BikeId, name: "demo bike");

        setupStore.Upsert(setup);
        bikeStore.Upsert(bike);
        database.GetAllAsync<Board>().Returns(Task.FromResult(new List<Board> { new(boardId, setup.Id) }));

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
    public async Task Changes_RebuildsProjection_WhenBikeStoreChanges()
    {
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "trail setup", bikeId: Guid.NewGuid(), boardId: boardId);
        var bike = TestSnapshots.Bike(id: setup.BikeId, name: "old bike name");

        setupStore.Upsert(setup);
        bikeStore.Upsert(bike);
        database.GetAllAsync<Board>().Returns(Task.FromResult(new List<Board> { new(boardId, setup.Id) }));

        using var query = CreateQuery();
        await WaitForRecordsAsync(query.Changes);

        var nextChange = WaitForRecordsAsync(query.Changes, ignoreReplay: true);
        bikeStore.Upsert(bike with { Name = "new bike name" });
        var records = await nextChange;

        var record = Assert.Single(records);
        Assert.Equal("new bike name", record.BikeName);
    }

    [Fact]
    public async Task Get_ReturnsLatestRecord_ByIdentityKey()
    {
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "known setup", bikeId: Guid.NewGuid(), boardId: boardId);
        var bike = TestSnapshots.Bike(id: setup.BikeId, name: "known bike");

        setupStore.Upsert(setup);
        bikeStore.Upsert(bike);
        database.GetAllAsync<Board>().Returns(Task.FromResult(new List<Board> { new(boardId, setup.Id) }));

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
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "calibrated setup", bikeId: bike.Id, boardId: boardId) with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 8,
                Resolution = 10,
            })
        };

        setupStore.Upsert(setup);
        bikeStore.Upsert(bike);
        database.GetAllAsync<Board>().Returns(Task.FromResult(new List<Board> { new(boardId, setup.Id) }));

        using var query = CreateQuery();
        await WaitForRecordsAsync(query.Changes);

        var calibration = query.GetTravelCalibration(boardId.ToString());

        Assert.NotNull(calibration);
        Assert.NotNull(calibration!.Front);
        Assert.Null(calibration.Rear);
        Assert.True(calibration.Front!.MaxTravel > 0);
        Assert.True(calibration.Front.MeasurementToTravel(1) > 0);
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
            Linkage = TestSnapshots.FullSuspensionLinkage(),
        };
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "session setup", bikeId: bike.Id, boardId: boardId) with
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

        setupStore.Upsert(setup);
        bikeStore.Upsert(bike);
        database.GetAllAsync<Board>().Returns(Task.FromResult(new List<Board> { new(boardId, setup.Id) }));

        using var query = CreateQuery();
        await WaitForRecordsAsync(query.Changes);

        var context = query.GetSessionContext(boardId.ToString());

        Assert.NotNull(context);
        Assert.Equal(boardId, context!.BoardId);
        Assert.Equal(setup.Id, context.SetupId);
        Assert.Equal("session setup", context.SetupName);
        Assert.Equal(bike.Id, context.BikeId);
        Assert.Equal("session bike", context.BikeName);
        Assert.Equal(bike.HeadAngle, context.BikeData.HeadAngle);
        Assert.NotNull(context.BikeData.FrontMeasurementToTravel);
        Assert.NotNull(context.BikeData.RearMeasurementToTravel);
        Assert.NotNull(context.TravelCalibration.Front);
        Assert.NotNull(context.TravelCalibration.Rear);
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

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class TestSetupStore : ISetupStore
    {
        private readonly SourceCache<SetupSnapshot, Guid> source = new(snapshot => snapshot.Id);

        public IObservable<IChangeSet<SetupSnapshot, Guid>> Connect() => source.Connect();

        public SetupSnapshot? Get(Guid id)
        {
            var lookup = source.Lookup(id);
            return lookup.HasValue ? lookup.Value : null;
        }

        public SetupSnapshot? FindByBoardId(Guid boardId) =>
            source.Items.FirstOrDefault(snapshot => snapshot.BoardId == boardId);

        public Task RefreshAsync() => Task.CompletedTask;

        public void Upsert(SetupSnapshot snapshot) => source.AddOrUpdate(snapshot);
    }

    private sealed class TestBikeStore : IBikeStore
    {
        private readonly SourceCache<BikeSnapshot, Guid> source = new(snapshot => snapshot.Id);

        public IObservable<IChangeSet<BikeSnapshot, Guid>> Connect() => source.Connect();

        public BikeSnapshot? Get(Guid id)
        {
            var lookup = source.Lookup(id);
            return lookup.HasValue ? lookup.Value : null;
        }

        public Task RefreshAsync() => Task.CompletedTask;

        public void Upsert(BikeSnapshot snapshot) => source.AddOrUpdate(snapshot);
    }
}