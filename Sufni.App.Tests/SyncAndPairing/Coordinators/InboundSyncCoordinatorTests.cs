using System.Reactive.Linq;
using NSubstitute;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Segments.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Sync;

namespace Sufni.App.Tests.SyncAndPairing.Coordinators;

public class InboundSyncCoordinatorTests
{
    [Theory]
    [InlineData("unknown_table", 6L)]
    [InlineData("board", null)]
    public async Task ApplyPushAsync_InvalidDeletionRejectsWholePushWithoutPublication(string table, long? deleted)
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var coordinator = fixture.CreateCoordinator();
        var board = new Board(Guid.NewGuid(), null) { Version = 1 };
        await coordinator.ApplyPushAsync(new SynchronizationData { Boards = [board] });

        var result = await coordinator.ApplyPushAsync(new SynchronizationData
        {
            Deletions =
            [
                new SynchronizationDeletion { EntityTable = "board", Id = board.Id, Version = 2, Updated = 6, Deleted = 6 },
                new SynchronizationDeletion { EntityTable = table, Id = Guid.NewGuid(), Version = 2, Updated = 6, Deleted = deleted }
            ]
        });

        Assert.Equal(InboundSyncWriteResult.InvalidPayload, result);
        Assert.NotNull(fixture.Boards.Get(board.Id));
        Assert.NotNull(await fixture.Database.GetAsync<Board>(board.Id));
        Assert.Empty((await fixture.Database.GetSynchronizationDataAsync(0, long.MaxValue)).Deletions);
    }

    [Fact]
    public async Task ApplySourceAsync_AwaitsPublication_AndPreservesExistingCapturedContent()
    {
        var dispatcher = new ManualUiThreadDispatcher();
        await using var fixture = new InboundSyncFixture(dispatcher);
        var coordinator = fixture.CreateCoordinator();
        var source = SyncTestServerHarness.CreateRecordedSource();
        var apply = coordinator.ApplySourceAsync(SyncTestServerHarness.ToPayload(source));
        await dispatcher.WaitForWorkAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.False(apply.IsCompleted);
        Assert.Null(fixture.Sources.Get(source.SessionId));
        await dispatcher.DrainAsync();
        Assert.Equal(InboundSourceApplyResult.Applied, await apply.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(RecordedSessionSourceSnapshot.From(source), fixture.Sources.Get(source.SessionId));

        var other = SyncTestServerHarness.ToPayload(SyncTestServerHarness.CreateRecordedSource([1, 2, 3])) with { SessionId = source.SessionId };
        var repeated = coordinator.ApplySourceAsync(other);
        await dispatcher.WaitForWorkAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await dispatcher.DrainAsync();
        Assert.Equal(InboundSourceApplyResult.Applied, await repeated.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(source.Payload, (await fixture.Database.GetRecordedSessionSourceAsync(source.SessionId))!.Payload);
        Assert.Equal(RecordedSessionSourceSnapshot.From(source), fixture.Sources.Get(source.SessionId));
    }

    [Fact]
    public async Task ApplySourceAsync_InvalidHashLeavesSourceMissing()
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var payload = SyncTestServerHarness.ToPayload(SyncTestServerHarness.CreateRecordedSource()) with { SourceHash = "wrong" };

        Assert.Equal(InboundSourceApplyResult.InvalidPayload, await fixture.CreateCoordinator().ApplySourceAsync(payload));

        Assert.Null(await fixture.Database.GetRecordedSessionSourceAsync(payload.SessionId));
        Assert.Null(fixture.Sources.Get(payload.SessionId));
    }

    [Fact]
    public async Task ApplySourceAsync_PersistenceFailureDoesNotPublishUncommittedSource()
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var repository = Substitute.For<IRecordedSessionSourceRepository>();
        repository.PutRecordedSessionSourceAsync(Arg.Any<RecordedSessionSource>())
            .Returns(Task.FromException<RecordedSessionSourceSnapshot>(new IOException("disk full")));
        var payload = SyncTestServerHarness.ToPayload(SyncTestServerHarness.CreateRecordedSource());

        Assert.Equal(InboundSourceApplyResult.Failed,
            await fixture.CreateCoordinator(sourceRepository: repository).ApplySourceAsync(payload));

        Assert.Null(fixture.Sources.Get(payload.SessionId));
    }

    [Fact]
    public async Task ApplyPushAsync_BoardOnlyArrivalRefreshesKnownBoardQuery()
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var coordinator = fixture.CreateCoordinator();
        var setup = new Setup(Guid.NewGuid(), "arrived setup");
        await coordinator.ApplyPushAsync(new SynchronizationData { Setups = [setup] });
        var board = new Board(Guid.NewGuid(), setup.Id);
        using var query = new LiveDaqKnownBoardsQuery(fixture.Boards, fixture.Setups, fixture.Bikes,
            new TelemetryBikeProcessingContextFactory(new RearTravelCalibrationBuilder(new KinematicSolutionCache())),
            new InlineBackgroundTaskRunner(), new InlineUiThreadDispatcher(), fixture.Publication);

        Assert.Equal(InboundSyncWriteResult.Applied,
            await coordinator.ApplyPushAsync(new SynchronizationData { Boards = [board] }));

        Assert.Equal("arrived setup", query.Get(board.Id.ToString())!.DisplayName);
    }

    [Fact]
    public async Task ApplyPushAsync_StaleDeleteRetainsCommittedSession()
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var coordinator = fixture.CreateCoordinator();
        var session = new Session(Guid.NewGuid(), "current", "", null) { Version = 3, Updated = 8 };
        await coordinator.ApplyPushAsync(new SynchronizationData { Sessions = [session] });

        Assert.Equal(InboundSyncWriteResult.Applied, await coordinator.ApplyPushAsync(new SynchronizationData
        {
            Sessions = [new Session { Id = session.Id, Version = 2, Updated = 6, Deleted = 6 }]
        }));

        Assert.Equal("current", fixture.Sessions.Get(session.Id)!.Name);
    }

    [Fact]
    public async Task ApplyPushAsync_MergeFailureLeavesPublishedStateUntouched_AndAllowsRetry()
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var dataStore = Substitute.For<ISyncDataStore>();
        var data = new SynchronizationData { Bikes = [new Bike(Guid.NewGuid(), "bike")] };
        dataStore.MergeAllAsync(data).Returns(_ => Task.FromException<SynchronizationApplyResult>(new IOException("database unavailable")));
        var coordinator = fixture.CreateCoordinator(dataStore);

        Assert.Equal(InboundSyncWriteResult.Failed, await coordinator.ApplyPushAsync(data));
        Assert.Null(fixture.Bikes.Get(data.Bikes[0].Id));

        dataStore.MergeAllAsync(data).Returns(_ => fixture.Database.SyncDataStore.MergeAllAsync(data));
        Assert.Equal(InboundSyncWriteResult.Applied, await coordinator.ApplyPushAsync(data));
        Assert.Equal("bike", fixture.Bikes.Get(data.Bikes[0].Id)!.Name);
    }
}
