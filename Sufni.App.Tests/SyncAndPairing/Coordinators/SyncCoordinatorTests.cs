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

        Assert.True(coordinator.IsRunning);
        Assert.Equal(SynchronizationPhase.PullingRemoteChanges, coordinator.Progress?.Phase);
        Assert.Equal(1.0 / 6, coordinator.Progress!.Fraction);
        harness.InboundActivity.OnNext(interrupted ? new PeerSyncActivity.Interrupted(id) : new PeerSyncActivity.Completed(id));

        Assert.False(coordinator.IsRunning);
        Assert.Null(coordinator.Progress);
        if (interrupted) Assert.Equal(Sufni.App.Shared.Models.SyncFailureReason.Interrupted, failure?.Reason);
        else Assert.Null(failure);
    }

    public void Dispose() => harness.Dispose();
}
