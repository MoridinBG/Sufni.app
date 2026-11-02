using NSubstitute;
using System.Reactive.Linq;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Sync;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.SyncAndPairing.Coordinators;

public class SyncCoordinatorTests : IDisposable
{
    private readonly SyncTestServerHarness harness = new();

    [Fact]
    public async Task UnpairedClient_CannotStartSync()
    {
        harness.PairingStates.OnNext(PairingClientState.Unpaired);
        var coordinator = harness.CreateCoordinator();

        Assert.False(coordinator.State.CanSync);
        Assert.False(coordinator.State.IsPaired);
        Assert.Null(await coordinator.RunSyncAsync());
        await harness.HttpApiService.DidNotReceive().PullSyncAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stop_CancelsActiveSyncWithoutFailure_AndDisablesFurtherRuns()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.HttpApiService.PullSyncAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            started.SetResult();
            return new TaskCompletionSource<SynchronizationData>().Task.WaitAsync(call.Arg<CancellationToken>());
        });
        var coordinator = harness.CreateCoordinator();
        var run = coordinator.RunSyncAsync();
        await started.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.True(coordinator.State.IsRunning);
        Assert.False(coordinator.State.CanSync);

        await coordinator.StopAsync().AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Null(await run);
        Assert.False(coordinator.State.IsRunning);
        Assert.False(coordinator.State.CanSync);
    }

    [Fact]
    public async Task RetiringCapturedPeer_CancelsRunWithoutFailure()
    {
        using var lifetime = new CancellationTokenSource();
        harness.HttpApiService.Lifetime.Returns(lifetime.Token);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.HttpApiService.PullSyncAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            started.SetResult();
            return new TaskCompletionSource<SynchronizationData>().Task.WaitAsync(call.Arg<CancellationToken>());
        });
        var coordinator = harness.CreateCoordinator();
        var run = coordinator.RunSyncAsync();
        await started.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        await lifetime.CancelAsync();

        Assert.Null(await run.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));
        Assert.False(coordinator.State.IsRunning);
    }

    [Fact]
    public async Task MissingDiscoveredPeer_ReportsUnavailableWithoutStartingTransfers()
    {
        harness.Pairing.ResolvePeerAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns((ISynchronizationPeer?)null);
        var coordinator = harness.CreateCoordinator();
        var unavailable = false;
        coordinator.ServerUnavailable += (_, _) => unavailable = true;

        Assert.Null(await coordinator.RunSyncAsync());

        Assert.True(unavailable);
        Assert.False(coordinator.State.IsRunning);
        Assert.Null(coordinator.State.Progress);
        await harness.HttpApiService.DidNotReceive().PullSyncAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void PairingChanges_NotifyAvailability()
    {
        var coordinator = harness.CreateCoordinator();
        var availability = new List<bool>();
        using var observation = coordinator.States.Skip(1).Subscribe(value => availability.Add(value.CanSync));

        harness.PairingStates.OnNext(PairingClientState.Unpaired);
        harness.PairingStates.OnNext(PairingClientState.Paired);

        Assert.Equal([false, true], availability);
    }

    [Fact]
    public async Task ConfirmationReportsPairingBeforeSyncFinishes_AndRetainsTheRunForShutdown()
    {
        var accepted = new TaskCompletionSource<ConfirmPairingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Pairing.ConfirmPairingAsync("phone", "123456").Returns(accepted.Task);
        var downloading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.HttpApiService.PullSyncAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            downloading.SetResult();
            return new TaskCompletionSource<SynchronizationData>().Task.WaitAsync(call.Arg<CancellationToken>());
        });
        var coordinator = harness.CreateCoordinator();
        var progress = new RecordingProgress<PairingClientState>();
        var confirmation = coordinator.ConfirmPairingAndSyncAsync("phone", "123456", progress);
        Assert.False(coordinator.State.CanSync);
        Assert.False(coordinator.State.IsRunning);
        await coordinator.SyncAllAsync();
        await harness.HttpApiService.DidNotReceive().PullSyncAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        accepted.SetResult(new ConfirmPairingResult.Paired());
        await downloading.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PairingClientState.Paired, Assert.Single(progress.Reports));
        Assert.False(confirmation.IsCompleted);

        await coordinator.StopAsync().AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<ConfirmPairingResult.Paired>(await confirmation);
        Assert.False(coordinator.State.CanSync);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InboundActivity_UsesDeclaredProgress_AndSettlesOnCompletionOrInterruption(bool interrupted)
    {
        var coordinator = harness.CreateCoordinator(inbound: harness.Inbound);
        var id = Guid.NewGuid();
        SyncFailedEventArgs? failure = null;
        coordinator.SyncFailed += (_, args) => failure = args;

        harness.InboundActivity.OnNext(new PeerSyncActivity.Running("phone", id,
            SynchronizationPhase.PullingRemoteChanges, 1, 6, harness.Time.GetUtcNow()));

        Assert.True(coordinator.State.IsRunning);
        Assert.Equal(SynchronizationPhase.PullingRemoteChanges, coordinator.State.Progress?.Phase);
        Assert.Equal(1.0 / 6, coordinator.State.Progress!.Fraction);
        harness.InboundActivity.OnNext(interrupted ? new PeerSyncActivity.Interrupted(id) : new PeerSyncActivity.Completed(id));

        Assert.False(coordinator.State.IsRunning);
        Assert.Null(coordinator.State.Progress);
        if (interrupted) Assert.Equal(Sufni.App.Shared.Models.SyncFailureReason.Interrupted, failure?.Reason);
        else Assert.Null(failure);
    }

    [Fact]
    public void StateReplaysCurrentPairing_AfterAllEarlierObserversLeave()
    {
        var coordinator = harness.CreateCoordinator();
        using (coordinator.States.Subscribe(_ => { })) { }
        harness.PairingStates.OnNext(PairingClientState.Unpaired);
        var replayed = new List<SyncState>();

        using var observation = coordinator.States.Subscribe(replayed.Add);

        Assert.False(Assert.Single(replayed).IsPaired);
        Assert.False(coordinator.State.CanSync);
    }

    [Fact]
    public async Task RePairingTheSameHub_ResetsCursorsBeforeConfirmation_AndPushesEveryLocalFact()
    {
        using var file = new TempDatabase();
        await using var database = new TestPersistenceHarness(file.DatabasePath);
        var board = new Board(Guid.NewGuid(), null);
        await database.PutAsync(board);
        await database.UpdateLastPushSequenceAsync(SyncTestServerHarness.Peer, 100);
        await database.UpdateLastPullSequenceAsync(SyncTestServerHarness.Peer, 100);
        harness.Pairing.ConfirmPairingAsync("phone", "123456").Returns(async _ =>
        {
            Assert.Equal(0, await database.GetLastPushSequenceAsync(SyncTestServerHarness.Peer));
            Assert.Equal(0, await database.GetLastPullSequenceAsync(SyncTestServerHarness.Peer));
            return (ConfirmPairingResult)new ConfirmPairingResult.Paired();
        });
        SynchronizationData? pushed = null;
        harness.HttpApiService.PushSyncAsync(Arg.Any<SynchronizationData>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            pushed = call.Arg<SynchronizationData>();
            return Task.CompletedTask;
        });
        var coordinator = harness.CreateCoordinator(database.SyncDataStore);

        Assert.IsType<ConfirmPairingResult.Paired>(await coordinator.ConfirmPairingAndSyncAsync("phone", "123456"));

        Assert.Equal(board.Id, Assert.Single(pushed!.Boards).Id);
        Assert.Equal(1, await database.GetLastPushSequenceAsync(SyncTestServerHarness.Peer));
    }

    [Fact]
    public async Task CursorResetFailure_DoesNotAcceptPairingOrStartSync()
    {
        harness.SyncDataStore.ResetCursorsAsync().Returns(Task.FromException(new IOException("database unavailable")));
        var coordinator = harness.CreateCoordinator();
        var progress = new RecordingProgress<PairingClientState>();

        var result = await coordinator.ConfirmPairingAndSyncAsync("phone", "123456", progress);

        Assert.IsType<ConfirmPairingResult.Failed>(result);
        Assert.Empty(progress.Reports);
        await harness.Pairing.DidNotReceive().ConfirmPairingAsync(Arg.Any<string?>(), Arg.Any<string>());
        await harness.HttpApiService.DidNotReceive().BeginRunAsync(Arg.Any<SynchronizationRunStart>(), Arg.Any<CancellationToken>());
    }

    public void Dispose() => harness.Dispose();
}
