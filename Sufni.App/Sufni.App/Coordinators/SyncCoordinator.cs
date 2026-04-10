using System;
using System.Threading.Tasks;
using Sufni.App.Services;
using Sufni.App.Stores;

namespace Sufni.App.Coordinators;

public sealed class SyncCoordinator : ISyncCoordinator
{
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
        if (!CanSync) return;
        if (synchronizationClientService is null) return;

        IsRunning = true;
        try
        {
            await synchronizationClientService.SyncAll();
            await bikeStore.RefreshAsync();
            await setupStore.RefreshAsync();
            await sessionStore.RefreshAsync();
            await pairedDeviceStore.RefreshAsync();
            SyncCompleted?.Invoke(this, new SyncCompletedEventArgs("Sync successful"));
        }
        catch (Exception e)
        {
            SyncFailed?.Invoke(this, new SyncFailedEventArgs($"Sync failed: {e.Message}"));
        }
        finally
        {
            IsRunning = false;
        }
    }
}
