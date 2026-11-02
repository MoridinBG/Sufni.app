using NSubstitute;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Sync;

namespace Sufni.App.Tests.SyncAndPairing.Coordinators;

public class SyncCoordinatorTests : IDisposable
{
    private readonly SyncTestServerHarness harness = new();

    [Fact]
    public async Task UnpairedClient_CannotStartSync()
    {
        harness.PairedState.OnNext(false);
        var coordinator = harness.CreateCoordinator();

        Assert.False(coordinator.CanSync);
        Assert.False(coordinator.IsPaired);
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
        Assert.True(coordinator.IsRunning);
        Assert.False(coordinator.CanSync);

        await coordinator.StopAsync().AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Null(await run);
        Assert.False(coordinator.IsRunning);
        Assert.False(coordinator.CanSync);
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
        Assert.False(coordinator.IsRunning);
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
        Assert.False(coordinator.IsRunning);
        Assert.Null(coordinator.Progress);
        await harness.HttpApiService.DidNotReceive().PullSyncAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void PairingChanges_NotifyAvailability()
    {
        var coordinator = harness.CreateCoordinator();
        var availability = new List<bool>();
        coordinator.CanSyncChanged += (_, _) => availability.Add(coordinator.CanSync);

        harness.PairedState.OnNext(false);
        harness.PairedState.OnNext(true);

        Assert.Equal([false, true], availability);
    }

    [Fact]
    public async Task PairingConfirmed_StartsSync()
    {
        var coordinator = harness.CreateCoordinator();
        var completion = new TaskCompletionSource<SynchronizationRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.SyncCompleted += (_, args) => completion.TrySetResult(args.Result);

        harness.Pairing.PairingConfirmed += Raise.Event();

        Assert.IsType<SynchronizationRunResult.Completed>(await completion.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ServerSyncActivity_DrivesRunningStateAndProgress()
    {
        var server = new TestSynchronizationServerService();
        var coordinator = harness.CreateCoordinator(server: server, idleGrace: TimeSpan.Zero);
        var progress = new SynchronizationProgressSnapshot(
            SynchronizationPhase.ReceivingChanges,
            "Receiving remote changes",
            CurrentStep: 0,
            TotalSteps: 0,
            IsDeterminate: false);

        server.RaiseSyncActivityStarted(progress);

        Assert.True(coordinator.IsRunning);
        var currentProgress = coordinator.Progress;
        Assert.NotNull(currentProgress);
        Assert.Equal(SynchronizationPhase.ReceivingChanges, currentProgress.Phase);
        Assert.Equal("Receiving remote changes", currentProgress.Message);
        Assert.Equal(1, currentProgress.CurrentStep);
        Assert.Equal(6, currentProgress.TotalSteps);
        Assert.True(currentProgress.IsDeterminate);

        server.RaiseSyncActivityEnded(progress);

        Assert.False(coordinator.IsRunning);
        Assert.Null(coordinator.Progress);
    }

    [Fact]
    public async Task ServerSyncActivity_RemainsRunningDuringInboundIdleGrace()
    {
        var server = new TestSynchronizationServerService();
        var time = harness.Time;
        var dispatcher = new ManualUiThreadDispatcher();
        var coordinator = harness.CreateCoordinator(server: server, dispatcher: dispatcher);
        var firstProgress = new SynchronizationProgressSnapshot(
            SynchronizationPhase.ReceivingChanges, "Receiving remote changes", 0, 0, false);
        var secondProgress = firstProgress with { Phase = SynchronizationPhase.ServingSessionData };

        server.RaiseSyncActivityStarted(firstProgress);
        server.RaiseSyncActivityEnded(firstProgress);
        await dispatcher.DrainAsync();
        time.Advance(TimeSpan.FromSeconds(9));
        Assert.True(coordinator.IsRunning);

        server.RaiseSyncActivityStarted(secondProgress);
        await dispatcher.DrainAsync();
        time.Advance(TimeSpan.FromSeconds(1));
        await dispatcher.WaitForWorkAsync(TestContext.Current.CancellationToken);
        await dispatcher.DrainAsync();

        Assert.True(coordinator.IsRunning);
        Assert.Equal(SynchronizationPhase.ServingSessionData, coordinator.Progress?.Phase);

        server.RaiseSyncActivityEnded(secondProgress);
        await dispatcher.DrainAsync();
        time.Advance(TimeSpan.FromSeconds(9));
        Assert.True(coordinator.IsRunning);
        time.Advance(TimeSpan.FromSeconds(1));
        await dispatcher.WaitForWorkAsync(TestContext.Current.CancellationToken);
        await dispatcher.DrainAsync();

        Assert.False(coordinator.IsRunning);
        Assert.Null(coordinator.Progress);
    }
    public void Dispose() => harness.Dispose();
}
