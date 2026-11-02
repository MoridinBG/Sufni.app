using System;
using System.Reactive.Linq;
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
    private readonly IDisposable? pairedStateSubscription;
    private readonly IDisposable? inboundActivitySubscription;
    private SyncActivity activity;
    private Task? activeRun;
    private SynchronizationProgressSnapshot? progress;

    public bool IsRunning => activity != SyncActivity.Idle;

    public bool IsPaired => pairingClientCoordinator?.IsPaired ?? false;
    public bool CanSync => !shutdown.IsCancellationRequested && !IsRunning && IsPaired;
    public SynchronizationProgressSnapshot? Progress
    {
        get => progress;
        private set
        {
            if (progress == value) return;
            progress = value;
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? IsRunningChanged;
    public event EventHandler? IsPairedChanged;
    public event EventHandler? CanSyncChanged;
    public event EventHandler? ProgressChanged;
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

        if (pairingClientCoordinator is not null)
        {
            pairedStateSubscription = pairingClientCoordinator.PairedState.Subscribe(_ => PublishPairedStateChangeOnUiThread());
            pairingClientCoordinator.PairingConfirmed += OnPairingConfirmed;
        }

        if (inboundSyncCoordinator is not null)
            inboundActivitySubscription = inboundSyncCoordinator.Activity.Subscribe(value =>
                uiThreadDispatcher.Post(() => ApplyInboundActivity(value)));
    }

    public async Task SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var run = await uiThreadDispatcher.InvokeAsync<Task>(() =>
        {
            if (!CanSync) return Task.CompletedTask;
            activeRun = SyncCoreAsync(cancellationToken);
            return activeRun;
        });
        await run;
    }

    private void OnPairingConfirmed(object? sender, EventArgs args) => _ = SyncAllAsync();

    public async Task StopAsync()
    {
        await shutdown.CancelAsync();
        var work = await uiThreadDispatcher.InvokeAsync<Task?>(() => activeRun);
        if (work is not null) await work;
    }

    public void Dispose()
    {
        pairedStateSubscription?.Dispose();
        if (pairingClientCoordinator is not null) pairingClientCoordinator.PairingConfirmed -= OnPairingConfirmed;
        inboundActivitySubscription?.Dispose();
        shutdown.Dispose();
    }

    private async Task SyncCoreAsync(CancellationToken cancellationToken)
    {
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token, cancellationToken);
        var token = runCancellation.Token;
        ISynchronizationPeer? peer = null;
        if (!CanSync)
        {
            logger.Verbose("Sync request ignored because synchronization is unavailable");
            return;
        }

        logger.Information("Starting synchronization");
        SetActivity(SyncActivity.Outbound);
        try
        {
            if (pairingClientCoordinator is not null)
            {
                Progress = new SynchronizationProgressSnapshot(
                    SynchronizationPhase.ResolvingServer,
                    "Finding sync server",
                    CurrentStep: 1,
                    TotalSteps: 7,
                    IsDeterminate: true);

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
                Progress = null;
                SetActivity(SyncActivity.Idle);
            });
        }
    }

    private void PublishPairedStateChangeOnUiThread()
    {
        if (uiThreadDispatcher.CheckAccess())
        {
            PublishPairedStateChange();
            return;
        }

        uiThreadDispatcher.Post(PublishPairedStateChange);
    }

    private void PublishPairedStateChange()
    {
        IsPairedChanged?.Invoke(this, EventArgs.Empty);
        CanSyncChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetActivity(SyncActivity value)
    {
        if (activity == value) return;
        activity = value;
        IsRunningChanged?.Invoke(this, EventArgs.Empty);
        CanSyncChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyInboundActivity(PeerSyncActivity value)
    {
        if (shutdown.IsCancellationRequested) return;
        if (value is PeerSyncActivity.Running running)
        {
            SetActivity(SyncActivity.Inbound);
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
            Progress = new SynchronizationProgressSnapshot(running.Phase, message, running.Step, running.PhaseCount, true);
        }
        else
        {
            SetActivity(SyncActivity.Idle);
            Progress = null;
            if (value is PeerSyncActivity.Interrupted)
                SyncFailed?.Invoke(this, new SyncFailedEventArgs(SyncFailureReason.Interrupted));
        }
    }
}

public sealed record SyncCompletedEventArgs(SynchronizationRunResult Result);
public sealed record SyncFailedEventArgs(SyncFailureReason Reason, string? Details = null);
