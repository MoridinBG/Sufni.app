using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Sufni.App.Services;
using Sufni.App.Stores;
using Serilog;

namespace Sufni.App.Coordinators;

public class SyncCoordinator
{
    private static readonly ILogger logger = Log.ForContext<SyncCoordinator>();
    private static readonly TimeSpan ServerDiscoveryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultInboundActivityIdleGrace = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FinalInboundActivityIdleGrace = TimeSpan.FromSeconds(2);

    private readonly IBikeStoreWriter bikeStore;
    private readonly ISetupStoreWriter setupStore;
    private readonly ISessionStoreWriter sessionStore;
    private readonly IRecordedSessionSourceStore recordedSessionSourceStore;
    private readonly IPairedDeviceStoreWriter pairedDeviceStore;
    private readonly ISynchronizationClientService? synchronizationClientService;
    private readonly IPairingClientCoordinator? pairingClientCoordinator;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly TimeSpan inboundActivityIdleGrace;
    private readonly TimeSpan finalInboundActivityIdleGrace;

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
        IBikeStoreWriter bikeStore,
        ISetupStoreWriter setupStore,
        ISessionStoreWriter sessionStore,
        IRecordedSessionSourceStore recordedSessionSourceStore,
        IPairedDeviceStoreWriter pairedDeviceStore,
        ISynchronizationClientService? synchronizationClientService = null,
        IPairingClientCoordinator? pairingClientCoordinator = null,
        ISynchronizationServerService? synchronizationServerService = null,
        IBackgroundTaskRunner? backgroundTaskRunner = null,
        TimeSpan? inboundActivityIdleGrace = null,
        TimeSpan? finalInboundActivityIdleGrace = null)
    {
        this.bikeStore = bikeStore;
        this.setupStore = setupStore;
        this.sessionStore = sessionStore;
        this.recordedSessionSourceStore = recordedSessionSourceStore;
        this.pairedDeviceStore = pairedDeviceStore;
        this.synchronizationClientService = synchronizationClientService;
        this.pairingClientCoordinator = pairingClientCoordinator;
        this.backgroundTaskRunner = backgroundTaskRunner ?? new BackgroundTaskRunner();
        this.inboundActivityIdleGrace = inboundActivityIdleGrace ?? DefaultInboundActivityIdleGrace;
        this.finalInboundActivityIdleGrace = finalInboundActivityIdleGrace ?? FinalInboundActivityIdleGrace;

        if (pairingClientCoordinator is not null)
        {
            pairingClientCoordinator.IsPairedChanged += (_, _) =>
            {
                IsPairedChanged?.Invoke(this, EventArgs.Empty);
                CanSyncChanged?.Invoke(this, EventArgs.Empty);
            };
            pairingClientCoordinator.PairingConfirmed += (_, _) => _ = SyncAllAsync();
        }

        if (synchronizationServerService is not null)
        {
            synchronizationServerService.SyncActivityStarted += OnSyncActivityStarted;
            synchronizationServerService.SyncActivityEnded += OnSyncActivityEnded;
        }
    }

    public virtual async Task SyncAllAsync()
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
            await backgroundTaskRunner.RunAsync(() =>
                synchronizationClientService.SyncAll(new SyncProgressReporter(ReportOutboundServiceProgress)));

            logger.Verbose("Refreshing local stores after synchronization");
            Progress = new SynchronizationProgressSnapshot(
                SynchronizationPhase.RefreshingLocalStores,
                "Refreshing local lists",
                CurrentStep: 8,
                TotalSteps: 8,
                IsDeterminate: true);
            await RefreshStoresOnUiThreadAsync();

            logger.Information("Synchronization completed");
            SyncCompleted?.Invoke(this, new SyncCompletedEventArgs("Sync successful"));
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
        if (Dispatcher.UIThread.CheckAccess())
        {
            Progress = snapshot;
            return;
        }

        Dispatcher.UIThread.Post(() => Progress = snapshot);
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
        Dispatcher.UIThread.Post(() =>
        {
            inboundIdleGeneration++;
            inboundActivityDepth++;
            SetInboundSyncRunning(true);
            Progress = NormalizeInboundProgress(e.Progress);
        });
    }

    private void OnSyncActivityEnded(object? sender, SynchronizationActivityEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
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
        await Task.Delay(idleGrace);
        await Dispatcher.UIThread.InvokeAsync(() => ClearInboundActivityIfIdle(generation));
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

    private async Task RefreshStoresOnUiThreadAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await RefreshStoresAsync();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(RefreshStoresAsync);
    }

    private async Task RefreshStoresAsync()
    {
        await bikeStore.RefreshAsync();
        await setupStore.RefreshAsync();
        await sessionStore.RefreshAsync();
        await recordedSessionSourceStore.RefreshAsync();
        await pairedDeviceStore.RefreshAsync();
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
