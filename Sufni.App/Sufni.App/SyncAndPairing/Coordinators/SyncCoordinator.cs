using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Infrastructure;
namespace Sufni.App.SyncAndPairing.Coordinators;

public class SyncCoordinator : ISyncCoordinator
{
    private static readonly ILogger logger = Log.ForContext<SyncCoordinator>();
    private static readonly TimeSpan ServerDiscoveryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultInboundActivityIdleGrace = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FinalInboundActivityIdleGrace = TimeSpan.FromSeconds(2);

    private readonly IAppStateRefreshOrchestrator appStateRefreshOrchestrator;
    private readonly ISynchronizationClientService? synchronizationClientService;
    private readonly IPairingClientCoordinator? pairingClientCoordinator;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly IUiThreadDispatcher uiThreadDispatcher;
    private readonly TimeSpan inboundActivityIdleGrace;
    private readonly TimeSpan finalInboundActivityIdleGrace;
    private readonly Func<TimeSpan, Task> inboundActivityDelayAsync;

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
    public bool CanSync => !IsRunning && IsPaired;
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

    public SyncCoordinator(
        IAppStateRefreshOrchestrator appStateRefreshOrchestrator,
        ISynchronizationClientService? synchronizationClientService = null,
        IPairingClientCoordinator? pairingClientCoordinator = null,
        ISynchronizationServerService? synchronizationServerService = null,
        IBackgroundTaskRunner? backgroundTaskRunner = null,
        TimeSpan? inboundActivityIdleGrace = null,
        TimeSpan? finalInboundActivityIdleGrace = null,
        IUiThreadDispatcher? uiThreadDispatcher = null,
        Func<TimeSpan, Task>? inboundActivityDelayAsync = null)
    {
        this.appStateRefreshOrchestrator = appStateRefreshOrchestrator;
        this.synchronizationClientService = synchronizationClientService;
        this.pairingClientCoordinator = pairingClientCoordinator;
        this.backgroundTaskRunner = backgroundTaskRunner ?? new BackgroundTaskRunner();
        this.uiThreadDispatcher = uiThreadDispatcher ?? new AvaloniaUiThreadDispatcher();
        this.inboundActivityIdleGrace = inboundActivityIdleGrace ?? DefaultInboundActivityIdleGrace;
        this.finalInboundActivityIdleGrace = finalInboundActivityIdleGrace ?? FinalInboundActivityIdleGrace;
        this.inboundActivityDelayAsync = inboundActivityDelayAsync ?? Task.Delay;

        if (pairingClientCoordinator is not null)
        {
            _ = pairingClientCoordinator.PairedState.Subscribe(_ => PublishPairedStateChangeOnUiThread());
            pairingClientCoordinator.PairingConfirmed += (_, _) => _ = SyncAllAsync();
        }

        if (synchronizationServerService is not null)
        {
            synchronizationServerService.SyncActivityStarted += OnSyncActivityStarted;
            synchronizationServerService.SyncActivityEnded += OnSyncActivityEnded;
        }
    }

    public async Task SyncAllAsync()
    {
        if (!CanSync)
        {
            logger.Verbose("Sync request ignored because synchronization is unavailable");
            return;
        }

        if (synchronizationClientService is null)
        {
            logger.Error("Sync request could not start because no synchronization client service is available");
            SyncFailed?.Invoke(this, new SyncFailedEventArgs("Sync failed: sync unavailable"));
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

                var serverUrl = await pairingClientCoordinator.ResolveServerUrlAsync(ServerDiscoveryTimeout);
                if (serverUrl is null)
                {
                    SyncFailed?.Invoke(this, new SyncFailedEventArgs("Sync failed: no server discovered"));
                    return;
                }
            }

            logger.Verbose("Running remote synchronization phases");
            var result = await backgroundTaskRunner.RunAsync(() =>
                synchronizationClientService.SyncAll(new SyncProgressReporter(ReportOutboundServiceProgress)));

            logger.Verbose("Refreshing local stores after synchronization");
            Progress = new SynchronizationProgressSnapshot(
                SynchronizationPhase.RefreshingLocalStores,
                "Refreshing local lists",
                CurrentStep: 8,
                TotalSteps: 8,
                IsDeterminate: true);

            if (result is SynchronizationRunResult.PartialApply partialApply)
            {
                await RefreshCoreStateOnUiThreadAsync();
                logger.Error("Synchronization partially applied: {ErrorMessage}", partialApply.ErrorMessage);
                SyncFailed?.Invoke(this, new SyncFailedEventArgs($"Sync partially applied: {partialApply.ErrorMessage}"));
                return;
            }

            await RefreshStateOnUiThreadAsync();
            var completionMessage = GetCompletionMessage(result);
            logger.Information("Synchronization completed: {CompletionMessage}", completionMessage);
            SyncCompleted?.Invoke(this, new SyncCompletedEventArgs(completionMessage));
        }
        catch (Exception e)
        {
            logger.Error(e, "Synchronization failed");
            SyncFailed?.Invoke(this, new SyncFailedEventArgs($"Sync failed: {e.Message}"));
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

        _ = ClearInboundActivityAfterDelayAsync(generation, idleGrace);
    }

    private async Task ClearInboundActivityAfterDelayAsync(int generation, TimeSpan idleGrace)
    {
        await inboundActivityDelayAsync(idleGrace);
        await uiThreadDispatcher.InvokeAsync(() => ClearInboundActivityIfIdle(generation));
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

    private async Task RefreshCoreStateOnUiThreadAsync()
    {
        if (uiThreadDispatcher.CheckAccess())
        {
            await appStateRefreshOrchestrator.RefreshCoreStateAsync();
            return;
        }

        await uiThreadDispatcher.InvokeAsync(() => appStateRefreshOrchestrator.RefreshCoreStateAsync());
    }

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
        return appStateRefreshOrchestrator.RefreshAllStateAsync();
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
public sealed record SyncFailedEventArgs(string ErrorMessage);
