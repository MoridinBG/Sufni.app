using System.Linq;
using System.Reactive;
using DynamicData;
using NSubstitute;
using Sufni.App.Models;
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
    public async Task GetCurrent_ReturnsBoardOnlyRecord_WhenBoardHasNoSetup()
    {
        var boardId = Guid.NewGuid();
        database.GetAllAsync<Board>().Returns(Task.FromResult(new List<Board> { new(boardId, null) }));

        using var query = CreateQuery();
        await WaitForChangeAsync(query.Changes);

        var record = Assert.Single(query.GetCurrent());
        Assert.Equal(boardId.ToString(), record.IdentityKey);
        Assert.Equal(boardId.ToString(), record.DisplayName);
        Assert.Equal(boardId.ToString(), record.BoardId);
        Assert.Null(record.SetupId);
        Assert.Null(record.SetupName);
        Assert.Null(record.BikeId);
        Assert.Null(record.BikeName);
    }

    [Fact]
    public async Task GetCurrent_ReturnsEnrichedRecord_WhenBoardMapsToSetupAndBike()
    {
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "park setup", bikeId: Guid.NewGuid(), boardId: boardId);
        var bike = TestSnapshots.Bike(id: setup.BikeId, name: "demo bike");

        setupStore.Upsert(setup);
        bikeStore.Upsert(bike);
        database.GetAllAsync<Board>().Returns(Task.FromResult(new List<Board> { new(boardId, setup.Id) }));

        using var query = CreateQuery();
        await WaitForChangeAsync(query.Changes);

        var record = Assert.Single(query.GetCurrent());
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
        await WaitForChangeAsync(query.Changes);

        var nextChange = WaitForChangeAsync(query.Changes, ignoreReplay: true);
        bikeStore.Upsert(bike with { Name = "new bike name" });
        await nextChange;

        var record = Assert.Single(query.GetCurrent());
        Assert.Equal("new bike name", record.BikeName);
    }

    private static async Task WaitForChangeAsync(IObservable<Unit> changes, bool ignoreReplay = false)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var seenReplay = !ignoreReplay;
        IDisposable? subscription = null;
        subscription = changes.Subscribe(_ =>
        {
            if (!seenReplay)
            {
                seenReplay = true;
                return;
            }

            subscription?.Dispose();
            tcs.TrySetResult(true);
        });

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
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