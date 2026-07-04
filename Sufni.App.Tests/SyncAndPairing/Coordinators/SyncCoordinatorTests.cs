using Avalonia.Headless.XUnit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Avalonia.Threading;
using System.Reactive.Subjects;

using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Doubles;
namespace Sufni.App.Tests.SyncAndPairing.Coordinators;

[Collection("Ui")]
public class SyncCoordinatorTests
{
    private readonly IAppStateRefreshOrchestrator appStateRefreshOrchestrator = Substitute.For<IAppStateRefreshOrchestrator>();
    private readonly ISynchronizationClientService syncClient = Substitute.For<ISynchronizationClientService>();
    private readonly IPairingClientCoordinator pairing = Substitute.For<IPairingClientCoordinator>();
    private readonly Subject<bool> pairedState = new();

    public SyncCoordinatorTests()
    {
        pairing.PairedState.Returns(pairedState);
        pairing.ResolveServerUrlAsync(Arg.Any<TimeSpan>())
            .Returns(Task.FromResult<string?>("https://sync.test"));
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>())
            .Returns(CompletedSync());
        appStateRefreshOrchestrator.RefreshAllStateAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    private SyncCoordinator CreateCoordinator(
        ISynchronizationClientService? syncClientOverride = null,
        IPairingClientCoordinator? pairingOverride = null,
        ISynchronizationServerService? serverOverride = null,
        IUiThreadDispatcher? uiThreadDispatcherOverride = null) =>
        new(
            appStateRefreshOrchestrator,
            syncClientOverride ?? syncClient,
            pairingOverride ?? pairing,
            serverOverride,
            backgroundTaskRunner: new InlineBackgroundTaskRunner(),
            inboundActivityIdleGrace: TimeSpan.Zero,
            uiThreadDispatcher: uiThreadDispatcherOverride);

    private void SetPairingState(bool isPaired)
    {
        pairing.IsPaired.Returns(isPaired);
        pairedState.OnNext(isPaired);
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

    [AvaloniaFact]
    public async Task CanSync_IsFalse_WhileSyncIsRunning()
    {
        SetPairingState(true);
        var gate = new TaskCompletionSource();
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>())
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

        await syncClient.DidNotReceive().SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>());
        await appStateRefreshOrchestrator.DidNotReceive().RefreshAllStateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAllAsync_IsNoOp_WhenSynchronizationClientServiceIsNull()
    {
        SetPairingState(true);
        // Bypass the helper — it uses `??` to fall back to the class-level
        // substitute, so a null override there wouldn't actually inject null.
        var coordinator = new SyncCoordinator(
            appStateRefreshOrchestrator,
            synchronizationClientService: null,
            pairingClientCoordinator: pairing,
            backgroundTaskRunner: new InlineBackgroundTaskRunner(),
            inboundActivityIdleGrace: TimeSpan.Zero);

        await coordinator.SyncAllAsync();

        await appStateRefreshOrchestrator.DidNotReceive().RefreshAllStateAsync(Arg.Any<CancellationToken>());
    }

    // ----- SyncAllAsync happy path -----

    [AvaloniaFact]
    public async Task SyncAllAsync_CallsSyncAll_AndRefreshesState()
    {
        SetPairingState(true);
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>()).Returns(CompletedSync());

        var coordinator = CreateCoordinator();
        await coordinator.SyncAllAsync();

        await syncClient.Received(1).SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>());
        await appStateRefreshOrchestrator.Received(1).RefreshAllStateAsync(Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task SyncAllAsync_RaisesEvents_AndLeavesIsRunningFalse()
    {
        SetPairingState(true);
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>()).Returns(CompletedSync());
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

    [AvaloniaFact]
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

    [AvaloniaFact]
    public async Task SyncAllAsync_RaisesIncompleteSyncCompletedMessage_WhenLocalDataRemainsIncomplete()
    {
        SetPairingState(true);
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>())
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
        await appStateRefreshOrchestrator.Received(1).RefreshAllStateAsync(Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task SyncAllAsync_ReportsOuterAndServiceProgress_AndClearsItAfterSuccess()
    {
        SetPairingState(true);
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>())
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

    [AvaloniaFact]
    public async Task SyncAllAsync_ClearsProgress_WhenNoServerEndpointIsDiscovered()
    {
        SetPairingState(true);
        pairing.ResolveServerUrlAsync(Arg.Any<TimeSpan>())
            .Returns(Task.FromResult<string?>(null));
        var coordinator = CreateCoordinator();
        var events = new List<SynchronizationProgressSnapshot?>();
        coordinator.ProgressChanged += (_, _) => events.Add(coordinator.Progress);

        await coordinator.SyncAllAsync();

        Assert.Contains(events, e => e?.Phase == SynchronizationPhase.ResolvingServer);
        Assert.Null(events.Last());
        Assert.Null(coordinator.Progress);
        Assert.False(coordinator.IsRunning);
    }

    // ----- SyncAllAsync failure -----

    [AvaloniaFact]
    public async Task SyncAllAsync_RaisesSyncFailed_AndResetsIsRunning_WhenSyncThrows()
    {
        SetPairingState(true);
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>()).ThrowsAsync(new InvalidOperationException("boom"));
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
        await appStateRefreshOrchestrator.DidNotReceive().RefreshAllStateAsync(Arg.Any<CancellationToken>());
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

    [AvaloniaFact]
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
        await syncCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await syncClient.Received(1).SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>());
        await appStateRefreshOrchestrator.Received(1).RefreshAllStateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void PairingConfirmed_DoesNotSync_WhenCanSyncIsFalse()
    {
        // IsPaired is false by default — the auto-sync silently no-ops.
        SetPairingState(false);
        var coordinator = CreateCoordinator();

        pairing.PairingConfirmed += Raise.Event();

        _ = syncClient.DidNotReceive().SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>());
    }

    [AvaloniaFact]
    public async Task SyncAllAsync_RefreshesState_OnUiThread()
    {
        SetPairingState(true);

        var refreshedOnUiThread = false;
        appStateRefreshOrchestrator
            .RefreshAllStateAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                refreshedOnUiThread = Dispatcher.UIThread.CheckAccess();
                return Task.CompletedTask;
            });

        await CreateCoordinator().SyncAllAsync();

        Assert.True(refreshedOnUiThread);
    }

    [AvaloniaFact]
    public async Task SyncAllAsync_ResolvesCurrentServerEndpoint_BeforeRunningSync()
    {
        SetPairingState(true);
        var coordinator = CreateCoordinator();

        await coordinator.SyncAllAsync();

        await pairing.Received(1).ResolveServerUrlAsync(Arg.Any<TimeSpan>());
        await syncClient.Received(1).SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>());
    }

    [AvaloniaFact]
    public async Task SyncAllAsync_RunsClientSyncThroughBackgroundRunner()
    {
        SetPairingState(true);
        syncClient.SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>()).Returns(CompletedSync());
        var backgroundTaskRunner = new RecordingBackgroundTaskRunner();
        var coordinator = new SyncCoordinator(
            appStateRefreshOrchestrator,
            syncClient,
            pairing,
            backgroundTaskRunner: backgroundTaskRunner,
            inboundActivityIdleGrace: TimeSpan.Zero);

        await coordinator.SyncAllAsync();

        Assert.Equal(1, backgroundTaskRunner.InvocationCount);
        await syncClient.Received(1).SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>());
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

    [AvaloniaFact]
    public async Task SyncAllAsync_RaisesSyncFailed_WhenNoServerEndpointIsDiscovered()
    {
        SetPairingState(true);
        pairing.ResolveServerUrlAsync(Arg.Any<TimeSpan>())
            .Returns(Task.FromResult<string?>(null));
        var coordinator = CreateCoordinator();

        var failed = 0;
        coordinator.SyncFailed += (_, _) => failed++;

        await coordinator.SyncAllAsync();

        Assert.Equal(1, failed);
        await syncClient.DidNotReceive().SyncAll(Arg.Any<IProgress<SynchronizationProgressSnapshot>?>());
        await appStateRefreshOrchestrator.DidNotReceive().RefreshAllStateAsync(Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task ServerSyncActivity_DrivesRunningStateAndProgress()
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
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.True(coordinator.IsRunning);
        var currentProgress = coordinator.Progress;
        Assert.NotNull(currentProgress);
        Assert.Equal(SynchronizationPhase.ReceivingChanges, currentProgress.Phase);
        Assert.Equal("Receiving remote changes", currentProgress.Message);
        Assert.Equal(1, currentProgress.CurrentStep);
        Assert.Equal(6, currentProgress.TotalSteps);
        Assert.True(currentProgress.IsDeterminate);

        server.RaiseSyncActivityEnded(progress);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.False(coordinator.IsRunning);
        Assert.Null(coordinator.Progress);
    }

    [AvaloniaFact]
    public async Task ServerSyncActivity_RemainsRunningDuringInboundIdleGrace()
    {
        var server = new TestSynchronizationServerService();
        var idleDelay = new ManualDelay();
        var coordinator = new SyncCoordinator(
            appStateRefreshOrchestrator,
            synchronizationServerService: server,
            backgroundTaskRunner: new InlineBackgroundTaskRunner(),
            inboundActivityIdleGrace: TimeSpan.FromMilliseconds(100),
            inboundActivityDelayAsync: idleDelay.DelayAsync);
        var firstProgress = new SynchronizationProgressSnapshot(
            SynchronizationPhase.ReceivingChanges,
            "Receiving remote changes",
            CurrentStep: 0,
            TotalSteps: 0,
            IsDeterminate: false);
        var secondProgress = new SynchronizationProgressSnapshot(
            SynchronizationPhase.ServingSessionData,
            "Serving session data",
            CurrentStep: 0,
            TotalSteps: 0,
            IsDeterminate: false);

        server.RaiseSyncActivityStarted(firstProgress);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        server.RaiseSyncActivityEnded(firstProgress);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.True(coordinator.IsRunning);
        Assert.Equal(TimeSpan.FromMilliseconds(100), idleDelay.PeekNextDelay());
        var currentProgress = coordinator.Progress;
        Assert.NotNull(currentProgress);
        Assert.Equal(SynchronizationPhase.ReceivingChanges, currentProgress.Phase);
        Assert.Equal(1, currentProgress.CurrentStep);
        Assert.Equal(6, currentProgress.TotalSteps);
        Assert.True(currentProgress.IsDeterminate);

        server.RaiseSyncActivityStarted(secondProgress);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await idleDelay.CompleteNextAsync();

        Assert.True(coordinator.IsRunning);
        currentProgress = coordinator.Progress;
        Assert.NotNull(currentProgress);
        Assert.Equal(SynchronizationPhase.ServingSessionData, currentProgress.Phase);
        Assert.Equal("Serving session data", currentProgress.Message);
        Assert.Equal(4, currentProgress.CurrentStep);
        Assert.Equal(6, currentProgress.TotalSteps);
        Assert.True(currentProgress.IsDeterminate);

        server.RaiseSyncActivityEnded(secondProgress);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.True(coordinator.IsRunning);
        Assert.Equal(TimeSpan.FromMilliseconds(100), idleDelay.PeekNextDelay());

        await idleDelay.CompleteNextAsync();

        Assert.False(coordinator.IsRunning);
        Assert.Null(coordinator.Progress);
    }

    private sealed class ManualDelay
    {
        private readonly Queue<PendingDelay> pending = [];

        public Task DelayAsync(TimeSpan delay)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Enqueue(new PendingDelay(delay, completion));
            return completion.Task;
        }

        public TimeSpan PeekNextDelay() => pending.Peek().Delay;

        public async Task CompleteNextAsync()
        {
            pending.Dequeue().Completion.SetResult();
            await Task.Yield();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }

        private sealed record PendingDelay(TimeSpan Delay, TaskCompletionSource Completion);
    }

}
