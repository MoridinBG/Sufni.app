using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.Shared.Models;
using NSubstitute.ExceptionExtensions;
using System.Reactive.Subjects;

using Sufni.App.Infrastructure;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Doubles;
namespace Sufni.App.Tests.SyncAndPairing.Coordinators;

public class SyncCoordinatorTests
{
    private readonly IAppDataRefresher appDataRefresher = Substitute.For<IAppDataRefresher>();
    private readonly ISynchronizationClientService syncClient = Substitute.For<ISynchronizationClientService>();
    private readonly IPairingClientCoordinator pairing = Substitute.For<IPairingClientCoordinator>();
    private readonly Subject<bool> pairedState = new();

    public SyncCoordinatorTests()
    {
        pairing.PairedState.Returns(pairedState);
        pairing.ResolveServerUrlAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("https://sync.test"));
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>(), Arg.Any<CancellationToken>())
            .Returns(CompletedSync());
        appDataRefresher.RefreshAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    private SyncCoordinator CreateCoordinator(
        ISynchronizationClientService? syncClientOverride = null,
        IPairingClientCoordinator? pairingOverride = null,
        ISynchronizationServerService? serverOverride = null,
        IUiThreadDispatcher? uiThreadDispatcherOverride = null) =>
        new(
            appDataRefresher,
            synchronizationClientService: syncClientOverride ?? syncClient,
            pairingClientCoordinator: pairingOverride ?? pairing,
            synchronizationServerService: serverOverride,
            backgroundTaskRunner: new InlineBackgroundTaskRunner(),
            inboundActivityIdleGrace: TimeSpan.Zero,
            uiThreadDispatcher: uiThreadDispatcherOverride ?? new InlineUiThreadDispatcher(),
            timeProvider: new FakeTimeProvider());

    private void SetPairingState(bool isPaired)
    {
        pairing.IsPaired.Returns(isPaired);
        pairedState.OnNext(isPaired);
    }

    [Fact]
    public async Task Stop_CancelsTheActiveSyncWithoutFailure_AndRefreshesCommittedState()
    {
        SetPairingState(true);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            started.SetResult();
            return new TaskCompletionSource<SynchronizationRunResult>().Task.WaitAsync(call.Arg<CancellationToken>());
        });
        using var coordinator = CreateCoordinator();
        var failed = false;
        coordinator.SyncFailed += (_, _) => failed = true;
        var run = coordinator.SyncAllAsync();
        await started.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        await coordinator.StopAsync().AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await run;

        Assert.False(coordinator.IsRunning);
        Assert.False(coordinator.CanSync);
        Assert.False(failed);
        await appDataRefresher.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
    }

    // ----- CanSync -----

    [Fact]
    public void CanSync_IsFalse_WhenPairingReportsNotPaired()
    {
        SetPairingState(false);

        var coordinator = CreateCoordinator();

        Assert.False(coordinator.CanSync);
        Assert.False(coordinator.IsPaired);
    }

    [Fact]
    public async Task CanSync_IsFalse_WhileSyncIsRunning()
    {
        SetPairingState(true);
        var gate = new TaskCompletionSource();
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>(), Arg.Any<CancellationToken>())
            .Returns(gate.Task.ContinueWith(_ => (SynchronizationRunResult)new SynchronizationRunResult.Completed()));

        var coordinator = CreateCoordinator();
        Assert.True(coordinator.CanSync);

        var running = coordinator.SyncAllAsync();
        Assert.True(coordinator.IsRunning);
        Assert.False(coordinator.CanSync);

        gate.SetResult();
        await running;

        Assert.False(coordinator.IsRunning);
        Assert.True(coordinator.CanSync);
    }

    // ----- SyncAllAsync no-ops -----

    [Fact]
    public async Task SyncAllAsync_IsNoOp_WhenCanSyncIsFalse()
    {
        SetPairingState(false);
        var coordinator = CreateCoordinator();

        await coordinator.SyncAllAsync();

        await syncClient.DidNotReceive().SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>(), Arg.Any<CancellationToken>());
        await appDataRefresher.DidNotReceive().RefreshAsync(Arg.Any<CancellationToken>());
    }

    // ----- SyncAllAsync happy path -----

    [Fact]
    public async Task SyncAllAsync_CallsSyncAll_AndRefreshesState()
    {
        SetPairingState(true);
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>(), Arg.Any<CancellationToken>()).Returns(CompletedSync());

        var coordinator = CreateCoordinator();
        await coordinator.SyncAllAsync();

        await syncClient.Received(1).SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>(), Arg.Any<CancellationToken>());
        await appDataRefresher.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAllAsync_RaisesEvents_AndLeavesIsRunningFalse()
    {
        SetPairingState(true);
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>(), Arg.Any<CancellationToken>()).Returns(CompletedSync());
        var coordinator = CreateCoordinator();

        var isRunningChanged = 0;
        var canSyncChanged = 0;
        coordinator.IsRunningChanged += (_, _) => isRunningChanged++;
        coordinator.CanSyncChanged += (_, _) => canSyncChanged++;

        await coordinator.SyncAllAsync();

        Assert.False(coordinator.IsRunning);
        Assert.True(isRunningChanged > 0);
        Assert.True(canSyncChanged > 0);
    }

    [Fact]
    public async Task SyncAllAsync_RaisesSyncCompleted_OnSuccess()
    {
        SetPairingState(true);
        var coordinator = CreateCoordinator();

        SyncCompletedEventArgs? completed = null;
        var failed = 0;
        coordinator.SyncCompleted += (_, args) => completed = args;
        coordinator.SyncFailed += (_, _) => failed++;

        await coordinator.SyncAllAsync();

        Assert.NotNull(completed);
        Assert.Equal("Sync successful", completed.Message);
        Assert.Equal(0, failed);
    }

    [Fact]
    public async Task SyncAllAsync_RaisesIncompleteSyncCompletedMessage_WhenLocalDataRemainsIncomplete()
    {
        SetPairingState(true);
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteSync(missingProcessedSessionCount: 2, incompleteRecordedSourceCount: 1));
        var coordinator = CreateCoordinator();

        SyncCompletedEventArgs? completed = null;
        var failed = 0;
        coordinator.SyncCompleted += (_, args) => completed = args;
        coordinator.SyncFailed += (_, _) => failed++;

        await coordinator.SyncAllAsync();

        Assert.NotNull(completed);
        Assert.Equal("Sync incomplete: 2 session blob(s) and 1 recorded source(s) still missing", completed.Message);
        Assert.Equal(0, failed);
        await appDataRefresher.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAllAsync_ReportsOuterAndServiceProgress_AndClearsItAfterSuccess()
    {
        SetPairingState(true);
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.ArgAt<IProgress<SynchronizationProgressSnapshot>?>(0)?.Report(
                    new SynchronizationProgressSnapshot(
                        SynchronizationPhase.PushingLocalChanges,
                        "Pushing local changes",
                        CurrentStep: 1,
                        TotalSteps: 6,
                        IsDeterminate: true));
                return CompletedSync();
            });
        var coordinator = CreateCoordinator();
        var events = new List<SynchronizationProgressSnapshot?>();
        coordinator.ProgressChanged += (_, _) => events.Add(coordinator.Progress);

        await coordinator.SyncAllAsync();

        Assert.Contains(events, e => e?.Phase == SynchronizationPhase.ResolvingServer);
        Assert.Contains(events, e =>
            e?.Phase == SynchronizationPhase.PushingLocalChanges &&
            e.CurrentStep == 2 &&
            e.TotalSteps == 8);
        Assert.Contains(events, e => e?.Phase == SynchronizationPhase.RefreshingLocalStores);
        Assert.Null(events.Last());
        Assert.Null(coordinator.Progress);
    }

    [Fact]
    public async Task SyncAllAsync_WhenNoServerEndpointIsDiscovered_ReportsUnavailableClearsProgressAndHasNoSideEffects()
    {
        SetPairingState(true);
        pairing.ResolveServerUrlAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));
        var coordinator = CreateCoordinator();
        var events = new List<SynchronizationProgressSnapshot?>();
        var failed = 0;
        var unavailable = 0;
        coordinator.ProgressChanged += (_, _) => events.Add(coordinator.Progress);
        coordinator.SyncFailed += (_, _) => failed++;
        coordinator.ServerUnavailable += (_, _) => unavailable++;

        await coordinator.SyncAllAsync();

        Assert.Equal(0, failed);
        Assert.Equal(1, unavailable);
        Assert.Contains(events, e => e?.Phase == SynchronizationPhase.ResolvingServer);
        Assert.Null(events.Last());
        Assert.Null(coordinator.Progress);
        Assert.False(coordinator.IsRunning);
        await syncClient.DidNotReceive().SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>(), Arg.Any<CancellationToken>());
        await appDataRefresher.DidNotReceive().RefreshAsync(Arg.Any<CancellationToken>());
    }

    // ----- SyncAllAsync failure -----

    [Fact]
    public async Task SyncAllAsync_RaisesSyncFailed_AndResetsIsRunning_WhenRunFails()
    {
        SetPairingState(true);
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>(), Arg.Any<CancellationToken>()).Returns(new SynchronizationRunResult.Failed(SyncFailureReason.Synchronize, "boom"));
        var coordinator = CreateCoordinator();

        var failed = 0;
        var completed = 0;
        coordinator.SyncFailed += (_, _) => failed++;
        coordinator.SyncCompleted += (_, _) => completed++;

        await coordinator.SyncAllAsync();

        Assert.Equal(1, failed);
        Assert.Equal(0, completed);
        Assert.False(coordinator.IsRunning);
        // Stores should not have been refreshed on failure.
        await appDataRefresher.DidNotReceive().RefreshAsync(Arg.Any<CancellationToken>());
    }

    // ----- Pairing state forwarding -----

    [Fact]
    public void PairingPairedState_ReRaisesIsPairedChanged_AndCanSyncChanged()
    {
        SetPairingState(false);
        var coordinator = CreateCoordinator(uiThreadDispatcherOverride: new InlineUiThreadDispatcher());

        var isPairedChanged = 0;
        var canSyncChanged = 0;
        coordinator.IsPairedChanged += (_, _) => isPairedChanged++;
        coordinator.CanSyncChanged += (_, _) => canSyncChanged++;

        SetPairingState(true);

        Assert.Equal(1, isPairedChanged);
        Assert.Equal(1, canSyncChanged);
    }

    [Fact]
    public async Task PairingConfirmed_KicksOffSyncAllAsync_Automatically()
    {
        // Pre-seed IsPaired = true so the fire-and-forget SyncAllAsync's
        // CanSync check passes.
        SetPairingState(true);
        var coordinator = CreateCoordinator();

        var syncCompleted = new TaskCompletionSource();
        coordinator.SyncCompleted += (_, _) => syncCompleted.TrySetResult();

        pairing.PairingConfirmed += Raise.Event();

        // Wait for the detached SyncAllAsync task to drive SyncCompleted.
        await syncCompleted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        await syncClient.Received(1).SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>(), Arg.Any<CancellationToken>());
        await appDataRefresher.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void PairingConfirmed_DoesNotSync_WhenCanSyncIsFalse()
    {
        // IsPaired is false by default — the auto-sync silently no-ops.
        SetPairingState(false);
        var coordinator = CreateCoordinator();

        pairing.PairingConfirmed += Raise.Event();

        _ = syncClient.DidNotReceive().SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAllAsync_RefreshesState_OnUiThread()
    {
        SetPairingState(true);

        var dispatcher = new RecordingUiThreadDispatcher(checkAccess: false);
        var refreshedOnUiThread = false;
        appDataRefresher
            .RefreshAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                refreshedOnUiThread = dispatcher.CheckAccess();
                return Task.CompletedTask;
            });

        await CreateCoordinator(uiThreadDispatcherOverride: dispatcher).SyncAllAsync();

        Assert.True(refreshedOnUiThread);
    }

    [Fact]
    public async Task SyncAllAsync_ResolvesCurrentServerEndpoint_BeforeRunningSync()
    {
        SetPairingState(true);
        var coordinator = CreateCoordinator();

        await coordinator.SyncAllAsync();

        await pairing.Received(1).ResolveServerUrlAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await syncClient.Received(1).SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAllAsync_RunsClientSyncThroughBackgroundRunner()
    {
        SetPairingState(true);
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>(), Arg.Any<CancellationToken>()).Returns(CompletedSync());
        var backgroundTaskRunner = new RecordingBackgroundTaskRunner();
        var coordinator = new SyncCoordinator(
            appDataRefresher,
            synchronizationClientService: syncClient,
            pairingClientCoordinator: pairing,
            backgroundTaskRunner: backgroundTaskRunner,
            inboundActivityIdleGrace: TimeSpan.Zero,
            uiThreadDispatcher: new InlineUiThreadDispatcher(),
            timeProvider: new FakeTimeProvider());

        await coordinator.SyncAllAsync();

        Assert.Equal(1, backgroundTaskRunner.InvocationCount);
        await syncClient.Received(1).SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>(), Arg.Any<CancellationToken>());
    }

    private static Task<SynchronizationRunResult> CompletedSync() =>
        Task.FromResult<SynchronizationRunResult>(new SynchronizationRunResult.Completed());

    private static Task<SynchronizationRunResult> IncompleteSync(
        int missingProcessedSessionCount,
        int incompleteRecordedSourceCount) =>
        Task.FromResult<SynchronizationRunResult>(
            new SynchronizationRunResult.IncompleteLocalData(
                missingProcessedSessionCount,
                incompleteRecordedSourceCount));

    [Fact]
    public void ServerSyncActivity_DrivesRunningStateAndProgress()
    {
        var server = new TestSynchronizationServerService();
        var coordinator = CreateCoordinator(serverOverride: server);
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
        var time = new FakeTimeProvider();
        var dispatcher = new ManualUiThreadDispatcher();
        var coordinator = new SyncCoordinator(
            appDataRefresher,
            new InlineBackgroundTaskRunner(),
            dispatcher,
            time,
            synchronizationServerService: server);
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
}
