using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Sufni.App.Services;
using Sufni.App.Stores;
using Serilog;

namespace Sufni.App.Coordinators;

public sealed class SyncCoordinator : ISyncCoordinator
{
    private static readonly ILogger logger = Log.ForContext<SyncCoordinator>();

    private readonly IBikeStoreWriter bikeStore;
    private readonly ISetupStoreWriter setupStore;
    private readonly ISessionStoreWriter sessionStore;
    private readonly IPairedDeviceStoreWriter pairedDeviceStore;
    private readonly ISynchronizationClientService? synchronizationClientService;
    private readonly IPairingClientCoordinator? pairingClientCoordinator;

    private bool isRunning;

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

    public event EventHandler? IsRunningChanged;
    public event EventHandler? IsPairedChanged;
    public event EventHandler? CanSyncChanged;
    public event EventHandler<SyncCompletedEventArgs>? SyncCompleted;
    public event EventHandler<SyncFailedEventArgs>? SyncFailed;

    public SyncCoordinator(
        IBikeStoreWriter bikeStore,
        ISetupStoreWriter setupStore,
        ISessionStoreWriter sessionStore,
        IPairedDeviceStoreWriter pairedDeviceStore,
        ISynchronizationClientService? synchronizationClientService = null,
        IPairingClientCoordinator? pairingClientCoordinator = null)
    {
        this.bikeStore = bikeStore;
        this.setupStore = setupStore;
        this.sessionStore = sessionStore;
        this.pairedDeviceStore = pairedDeviceStore;
        this.synchronizationClientService = synchronizationClientService;
        this.pairingClientCoordinator = pairingClientCoordinator;

        if (pairingClientCoordinator is not null)
        {
            pairingClientCoordinator.IsPairedChanged += (_, _) =>
            {
                IsPairedChanged?.Invoke(this, EventArgs.Empty);
                CanSyncChanged?.Invoke(this, EventArgs.Empty);
            };
            pairingClientCoordinator.PairingConfirmed += (_, _) => _ = SyncAllAsync();
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
        IsRunning = true;
        try
        {
            logger.Verbose("Running remote synchronization phases");
            await synchronizationClientService.SyncAll();

            logger.Verbose("Refreshing local stores after synchronization");
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
            IsRunning = false;
        }
    }

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
        await pairedDeviceStore.RefreshAsync();
    }
}
