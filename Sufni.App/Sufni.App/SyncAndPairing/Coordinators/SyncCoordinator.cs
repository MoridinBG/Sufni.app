using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using Sufni.App.Shared.Models;

using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Infrastructure;
namespace Sufni.App.SyncAndPairing.Coordinators;

public class SyncCoordinator : ISyncCoordinator, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<SyncCoordinator>();
    private static readonly TimeSpan ServerDiscoveryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultInboundActivityIdleGrace = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FinalInboundActivityIdleGrace = TimeSpan.FromSeconds(2);

    private readonly IAppDataRefresher appDataRefresher;
    private readonly ISynchronizationClientService? synchronizationClientService;
    private readonly IPairingClientCoordinator? pairingClientCoordinator;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly IUiThreadDispatcher uiThreadDispatcher;
    private readonly TimeSpan inboundActivityIdleGrace;
    private readonly TimeSpan finalInboundActivityIdleGrace;
    private readonly TimeProvider timeProvider;

    private readonly CancellationTokenSource shutdown = new();
    private readonly HashSet<Task> idleTasks = [];
    private readonly IDisposable? pairedStateSubscription;
    private readonly ISynchronizationServerService? synchronizationServer;
    private Task? activeRun;
    private bool isRunning;
    private bool outboundSyncRunning;
    private bool inboundSyncRunning;
    private int inboundActivityDepth;
    private int inboundIdleGeneration;
    private SynchronizationProgressSnapshot? progress;

    public bool IsRunning
    {
        get => isRunning;
        private set
        {
            if (isRunning == value) return;
            isRunning = value;
            IsRunningChanged?.Invoke(this, EventArgs.Empty);
            CanSyncChanged?.Invoke(this, EventArgs.Empty);
        }
    }

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
        IAppDataRefresher appDataRefresher,
        IBackgroundTaskRunner backgroundTaskRunner,
        IUiThreadDispatcher uiThreadDispatcher,
        TimeProvider timeProvider,
        ISynchronizationClientService? synchronizationClientService = null,
        IPairingClientCoordinator? pairingClientCoordinator = null,
        ISynchronizationServerService? synchronizationServerService = null,
        TimeSpan? inboundActivityIdleGrace = null,
        TimeSpan? finalInboundActivityIdleGrace = null)
    {
        this.appDataRefresher = appDataRefresher;
        synchronizationServer = synchronizationServerService;
        this.synchronizationClientService = synchronizationClientService;
        this.pairingClientCoordinator = pairingClientCoordinator;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.uiThreadDispatcher = uiThreadDispatcher;
        this.inboundActivityIdleGrace = inboundActivityIdleGrace ?? DefaultInboundActivityIdleGrace;
        this.finalInboundActivityIdleGrace = finalInboundActivityIdleGrace ?? FinalInboundActivityIdleGrace;
        this.timeProvider = timeProvider;

        if (pairingClientCoordinator is not null)
        {
            pairedStateSubscription = pairingClientCoordinator.PairedState.Subscribe(_ => PublishPairedStateChangeOnUiThread());
            pairingClientCoordinator.PairingConfirmed += OnPairingConfirmed;
        }

        if (synchronizationServerService is not null)
        {
            synchronizationServerService.SyncActivityStarted += OnSyncActivityStarted;
            synchronizationServerService.SyncActivityEnded += OnSyncActivityEnded;
        }
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
        var work = await uiThreadDispatcher.InvokeAsync(() =>
            idleTasks.Concat(activeRun is null ? [] : new[] { activeRun }).ToArray());
        await Task.WhenAll(work);
    }

    public void Dispose()
    {
        pairedStateSubscription?.Dispose();
        if (pairingClientCoordinator is not null) pairingClientCoordinator.PairingConfirmed -= OnPairingConfirmed;
        if (synchronizationServer is not null)
        {
            synchronizationServer.SyncActivityStarted -= OnSyncActivityStarted;
            synchronizationServer.SyncActivityEnded -= OnSyncActivityEnded;
        }
        shutdown.Dispose();
    }

    private async Task SyncCoreAsync(CancellationToken cancellationToken)
    {
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token, cancellationToken);
        var token = runCancellation.Token;
        if (!CanSync)
        {
            logger.Verbose("Sync request ignored because synchronization is unavailable");
            return;
        }

        logger.Information("Starting synchronization");
        SetOutboundSyncRunning(true);
        try
        {
            if (pairingClientCoordinator is not null)
            {
                Progress = new SynchronizationProgressSnapshot(
                    SynchronizationPhase.ResolvingServer,
                    "Finding sync server",
                    CurrentStep: 1,
                    TotalSteps: 8,
                    IsDeterminate: true);

                var serverUrl = await pairingClientCoordinator.ResolveServerUrlAsync(ServerDiscoveryTimeout, token);
                if (serverUrl is null)
                {
                    ServerUnavailable?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }

            logger.Verbose("Running remote synchronization phases");
            var result = await backgroundTaskRunner.RunAsync(() =>
                synchronizationClientService!.SyncAll(new SyncProgressReporter(ReportOutboundServiceProgress), token));

            if (result is SynchronizationRunResult.Failed failed)
            {
                SyncFailed?.Invoke(this, new SyncFailedEventArgs(failed.Reason, failed.Details));
                return;
            }

            logger.Verbose("Refreshing local stores after synchronization");
            Progress = new SynchronizationProgressSnapshot(
                SynchronizationPhase.RefreshingLocalStores,
                "Refreshing local lists",
                CurrentStep: 8,
                TotalSteps: 8,
                IsDeterminate: true);

            await RefreshStateOnUiThreadAsync();
            var completionMessage = GetCompletionMessage(result);
            logger.Information("Synchronization completed: {CompletionMessage}", completionMessage);
            SyncCompleted?.Invoke(this, new SyncCompletedEventArgs(completionMessage));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Already committed phases still need to reach the shared read state.
            await RefreshStateOnUiThreadAsync();
        }
        catch (Exception e)
        {
            logger.Error(e, "Synchronization failed during {Phase}", Progress?.Phase);
            SyncFailed?.Invoke(this, new SyncFailedEventArgs(SyncFailureReason.Synchronize, e.Message));
        }
        finally
        {
            if (!inboundSyncRunning)
            {
                Progress = null;
            }

            SetOutboundSyncRunning(false);
        }
    }

    private void ReportOutboundServiceProgress(SynchronizationProgressSnapshot snapshot)
    {
        SetProgressOnUiThread(snapshot with
        {
            CurrentStep = snapshot.CurrentStep + 1,
            TotalSteps = 8
        });
    }

    private void SetProgressOnUiThread(SynchronizationProgressSnapshot? snapshot)
    {
        if (uiThreadDispatcher.CheckAccess())
        {
            Progress = snapshot;
            return;
        }

        uiThreadDispatcher.Post(() => Progress = snapshot);
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

    private void SetOutboundSyncRunning(bool value)
    {
        outboundSyncRunning = value;
        UpdateIsRunning();
    }

    private void SetInboundSyncRunning(bool value)
    {
        inboundSyncRunning = value;
        UpdateIsRunning();
    }

    private void UpdateIsRunning()
    {
        IsRunning = outboundSyncRunning || inboundSyncRunning;
    }

    private void OnSyncActivityStarted(object? sender, SynchronizationActivityEventArgs e)
    {
        uiThreadDispatcher.Post(() =>
        {
            if (shutdown.IsCancellationRequested) return;
            inboundIdleGeneration++;
            inboundActivityDepth++;
            SetInboundSyncRunning(true);
            Progress = NormalizeInboundProgress(e.Progress);
        });
    }

    private void OnSyncActivityEnded(object? sender, SynchronizationActivityEventArgs e)
    {
        uiThreadDispatcher.Post(() =>
        {
            if (shutdown.IsCancellationRequested) return;
            if (inboundActivityDepth > 0)
            {
                inboundActivityDepth--;
            }

            if (inboundActivityDepth > 0)
            {
                return;
            }

            ScheduleInboundActivityIdleClear(GetInboundActivityIdleGrace());
        });
    }

    private void ScheduleInboundActivityIdleClear(TimeSpan idleGrace)
    {
        var generation = ++inboundIdleGeneration;
        if (idleGrace <= TimeSpan.Zero)
        {
            ClearInboundActivityIfIdle(generation);
            return;
        }

        var task = ClearInboundActivityAfterDelayAsync(generation, idleGrace);
        idleTasks.Add(task);
        _ = RemoveIdleTaskAsync(task);
    }

    private async Task ClearInboundActivityAfterDelayAsync(int generation, TimeSpan idleGrace)
    {
        try
        {
            await Task.Delay(idleGrace, timeProvider, shutdown.Token);
            await uiThreadDispatcher.InvokeAsync(() => ClearInboundActivityIfIdle(generation));
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
    }

    private async Task RemoveIdleTaskAsync(Task task)
    {
        await task;
        await uiThreadDispatcher.InvokeAsync(() => idleTasks.Remove(task));
    }

    private void ClearInboundActivityIfIdle(int generation)
    {
        if (generation != inboundIdleGeneration || inboundActivityDepth > 0)
        {
            return;
        }

        SetInboundSyncRunning(false);
        if (!outboundSyncRunning)
        {
            Progress = null;
        }
    }

    private TimeSpan GetInboundActivityIdleGrace()
    {
        return Progress is { IsDeterminate: true, CurrentStep: >= 5 }
            ? finalInboundActivityIdleGrace
            : inboundActivityIdleGrace;
    }

    private static SynchronizationProgressSnapshot NormalizeInboundProgress(SynchronizationProgressSnapshot progress) =>
        progress.Phase switch
        {
            SynchronizationPhase.ReceivingChanges => InboundProgress(
                progress,
                "Receiving remote changes",
                currentStep: 1),
            SynchronizationPhase.ServingChanges => InboundProgress(
                progress,
                "Serving remote changes",
                currentStep: 2),
            SynchronizationPhase.CheckingIncompleteSessions or SynchronizationPhase.ReceivingSessionData => InboundProgress(
                progress,
                "Receiving session data",
                currentStep: 3),
            SynchronizationPhase.ServingSessionData => InboundProgress(
                progress,
                "Serving session data",
                currentStep: 4),
            SynchronizationPhase.CheckingIncompleteSessionSources or SynchronizationPhase.ReceivingSessionSourceData => InboundProgress(
                progress,
                "Receiving recorded sources",
                currentStep: 5),
            SynchronizationPhase.ServingSessionSourceData => InboundProgress(
                progress,
                "Serving recorded sources",
                currentStep: 6),
            _ => progress
        };

    private static SynchronizationProgressSnapshot InboundProgress(
        SynchronizationProgressSnapshot progress,
        string message,
        int currentStep) =>
        progress with
        {
            Message = message,
            CurrentStep = currentStep,
            TotalSteps = 6,
            IsDeterminate = true
        };

    private static string GetCompletionMessage(SynchronizationRunResult result) =>
        result switch
        {
            SynchronizationRunResult.Completed => "Sync successful",
            SynchronizationRunResult.IncompleteLocalData incomplete =>
                $"Sync incomplete: {incomplete.MissingProcessedSessionCount} session blob(s) and {incomplete.IncompleteRecordedSourceCount} recorded source(s) still missing",
            _ => "Sync completed"
        };

    private async Task RefreshStateOnUiThreadAsync()
    {
        if (uiThreadDispatcher.CheckAccess())
        {
            await RefreshStateAsync();
            return;
        }

        await uiThreadDispatcher.InvokeAsync(RefreshStateAsync);
    }

    private Task RefreshStateAsync()
    {
        return appDataRefresher.RefreshAsync();
    }

    private sealed class SyncProgressReporter(Action<SynchronizationProgressSnapshot> report)
        : IProgress<SynchronizationProgressSnapshot>
    {
        public void Report(SynchronizationProgressSnapshot value)
        {
            report(value);
        }
    }
}

public sealed record SyncCompletedEventArgs(string Message);
public sealed record SyncFailedEventArgs(SyncFailureReason Reason, string? Details = null);
