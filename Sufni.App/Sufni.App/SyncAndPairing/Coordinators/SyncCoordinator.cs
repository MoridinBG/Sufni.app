using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using Sufni.App.Shared.Models;
using Sufni.App.SyncAndPairing.Models;

using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Sessions.Coordination;
namespace Sufni.App.SyncAndPairing.Coordinators;

public partial class SyncCoordinator : ISyncCoordinator, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<SyncCoordinator>();
    private static readonly TimeSpan ServerDiscoveryTimeout = TimeSpan.FromSeconds(5);
    private readonly IPairingClientCoordinator? pairingClientCoordinator;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly IUiThreadDispatcher uiThreadDispatcher;

    private readonly CancellationTokenSource shutdown = new();
    private readonly IDisposable? inboundActivitySubscription;
    private readonly BehaviorSubject<(SyncActivity Activity, SynchronizationProgressSnapshot? Progress)> operation = new((SyncActivity.Idle, null));
    private Task? activeRun;

    public SyncState State => new(pairingClientCoordinator?.State ?? PairingClientState.Unpaired,
        operation.Value.Activity, operation.Value.Progress);
    public IObservable<SyncState> States { get; }
    private readonly IDisposable stateConnection;
    private SynchronizationProgressSnapshot? Progress
    {
        get => operation.Value.Progress;
        set => SetOperation(operation.Value.Activity, value);
    }

    public event EventHandler<SyncCompletedEventArgs>? SyncCompleted;
    public event EventHandler<SyncFailedEventArgs>? SyncFailed;
    public event EventHandler? ServerUnavailable;

    public SyncCoordinator(
        ISyncDataStore syncDataStore,
        ISynchronizationApplier rowApplier,
        ISessionRepository sessionRepository,
        ISessionSyncApplier sessionSyncApplier,
        IRecordedSessionSourceRepository recordedSessionSourceRepository,
        IRecordedSessionSourceStoreWriter sourceStore,
        IRecordedSessionSourceSyncQuery recordedSessionSourceSyncQuery,
        IBackgroundTaskRunner backgroundTaskRunner,
        IUiThreadDispatcher uiThreadDispatcher,
        IPairingClientCoordinator? pairingClientCoordinator = null,
        IInboundSyncCoordinator? inboundSyncCoordinator = null)
    {
        this.syncDataStore = syncDataStore;
        this.rowApplier = rowApplier;
        this.sessionRepository = sessionRepository;
        this.sessionSyncApplier = sessionSyncApplier;
        this.recordedSessionSourceRepository = recordedSessionSourceRepository;
        this.sourceStore = sourceStore;
        this.recordedSessionSourceSyncQuery = recordedSessionSourceSyncQuery;
        this.pairingClientCoordinator = pairingClientCoordinator;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.uiThreadDispatcher = uiThreadDispatcher;

        var states = (pairingClientCoordinator?.States ?? Observable.Return(PairingClientState.Unpaired))
            .CombineLatest(operation, (pairing, current) => new SyncState(pairing, current.Activity, current.Progress))
            .DistinctUntilChanged().Replay(1);
        States = states;
        stateConnection = states.Connect();

        if (inboundSyncCoordinator is not null)
            inboundActivitySubscription = inboundSyncCoordinator.Activity.Subscribe(value =>
                uiThreadDispatcher.Post(() => ApplyInboundActivity(value)));
    }

    public async Task SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var run = await uiThreadDispatcher.InvokeAsync<Task>(() =>
        {
            if (!State.CanSync) return Task.CompletedTask;
            activeRun = SyncCoreAsync(cancellationToken);
            return activeRun;
        });
        await run;
    }

    public async Task<ConfirmPairingResult> ConfirmPairingAndSyncAsync(string? displayName, string pin,
        IProgress<PairingClientState>? progress = null)
    {
        var confirmation = await uiThreadDispatcher.InvokeAsync<Task<ConfirmPairingResult>>(() =>
        {
            var task = ConfirmAndSyncCoreAsync(activeRun, displayName, pin, progress);
            activeRun = task;
            return task;
        });
        return await confirmation;
    }

    private async Task<ConfirmPairingResult> ConfirmAndSyncCoreAsync(Task? previousRun, string? displayName,
        string pin, IProgress<PairingClientState>? progress)
    {
        if (previousRun is not null) await previousRun;
        await uiThreadDispatcher.InvokeAsync(() => SetOperation(SyncActivity.Pairing, null));
        try
        {
            try
            {
                // This installation pairs with one hub. New pairing always starts a baseline.
                await backgroundTaskRunner.RunAsync(syncDataStore.ResetCursorsAsync);
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Failed to reset synchronization cursors before pairing");
                return new ConfirmPairingResult.Failed(PairingFailureReason.Confirm, exception.Message);
            }
            var result = await pairingClientCoordinator!.ConfirmPairingAsync(displayName, pin);
            if (result is ConfirmPairingResult.Paired && !shutdown.IsCancellationRequested)
            {
                progress?.Report(PairingClientState.Paired);
                await SyncCoreAsync(shutdown.Token);
            }
            return result;
        }
        finally
        {
            await uiThreadDispatcher.InvokeAsync(() =>
            {
                if (operation.Value.Activity == SyncActivity.Pairing)
                    SetOperation(shutdown.IsCancellationRequested ? SyncActivity.Stopped : SyncActivity.Idle, null);
            });
        }
    }

    public async Task StopAsync()
    {
        await shutdown.CancelAsync();
        var work = await uiThreadDispatcher.InvokeAsync<Task?>(() => activeRun);
        if (work is not null) await work;
        await uiThreadDispatcher.InvokeAsync(() => SetOperation(SyncActivity.Stopped, null));
    }

    public void Dispose()
    {
        stateConnection.Dispose();
        inboundActivitySubscription?.Dispose();
        operation.Dispose();
        shutdown.Dispose();
    }

    private async Task SyncCoreAsync(CancellationToken cancellationToken)
    {
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token, cancellationToken);
        var token = runCancellation.Token;
        ISynchronizationPeer? peer = null;
        logger.Information("Starting synchronization");
        await uiThreadDispatcher.InvokeAsync(() => SetOperation(SyncActivity.Outbound,
            new SynchronizationProgressSnapshot(SynchronizationPhase.ResolvingServer, "Finding sync server", 1, 7, true)));
        try
        {
            if (pairingClientCoordinator is not null)
            {
                peer = await pairingClientCoordinator.ResolvePeerAsync(ServerDiscoveryTimeout, token);
                if (peer is null)
                {
                    await uiThreadDispatcher.InvokeAsync(() => ServerUnavailable?.Invoke(this, EventArgs.Empty));
                    return;
                }
            }

            logger.Verbose("Running remote synchronization phases");
            var result = await backgroundTaskRunner.RunAsync(() =>
                RunClientAsync(peer!, token));

            if (result is SynchronizationRunResult.Failed failed)
            {
                await uiThreadDispatcher.InvokeAsync(() => SyncFailed?.Invoke(this, new SyncFailedEventArgs(failed.Reason, failed.Details)));
                return;
            }

            logger.Information("Synchronization completed with {Outcome}", result.GetType().Name);
            await uiThreadDispatcher.InvokeAsync(() => SyncCompleted?.Invoke(this, new SyncCompletedEventArgs(result)));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || peer?.Lifetime.IsCancellationRequested == true) { }
        catch (Exception e)
        {
            logger.Error(e, "Synchronization failed during {Phase}", Progress?.Phase);
            await uiThreadDispatcher.InvokeAsync(() => SyncFailed?.Invoke(this, new SyncFailedEventArgs(SyncFailureReason.Synchronize, e.Message)));
        }
        finally
        {
            await uiThreadDispatcher.InvokeAsync(() =>
            {
                SetOperation(shutdown.IsCancellationRequested ? SyncActivity.Stopped : SyncActivity.Idle, null);
            });
        }
    }

    private void SetOperation(SyncActivity activity, SynchronizationProgressSnapshot? progress)
    {
        if (operation.Value == (activity, progress)) return;
        operation.OnNext((activity, progress));
    }

    private void ApplyInboundActivity(PeerSyncActivity value)
    {
        if (shutdown.IsCancellationRequested) return;
        if (value is PeerSyncActivity.Running running)
        {
            var message = running.Phase switch
            {
                SynchronizationPhase.PushingLocalChanges => "Receiving remote changes",
                SynchronizationPhase.PullingRemoteChanges => "Serving remote changes",
                SynchronizationPhase.PushingIncompleteSessions => "Receiving session data",
                SynchronizationPhase.PullingIncompleteSessions => "Serving session data",
                SynchronizationPhase.PushingIncompleteSessionSources => "Receiving recorded sources",
                SynchronizationPhase.PullingIncompleteSessionSources => "Serving recorded sources",
                _ => "Syncing"
            };
            SetOperation(SyncActivity.Inbound, new SynchronizationProgressSnapshot(running.Phase, message, running.Step, running.PhaseCount, true));
        }
        else
        {
            SetOperation(SyncActivity.Idle, null);
            if (value is PeerSyncActivity.Interrupted)
                SyncFailed?.Invoke(this, new SyncFailedEventArgs(SyncFailureReason.Interrupted));
        }
    }
}

public sealed record SyncCompletedEventArgs(SynchronizationRunResult Result);
public sealed record SyncFailedEventArgs(SyncFailureReason Reason, string? Details = null);
