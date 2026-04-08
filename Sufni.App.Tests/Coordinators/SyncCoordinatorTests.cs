using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.Stores;

namespace Sufni.App.Tests.Coordinators;

public class SyncCoordinatorTests
{
    private readonly IBikeStoreWriter bikeStore = Substitute.For<IBikeStoreWriter>();
    private readonly ISetupStoreWriter setupStore = Substitute.For<ISetupStoreWriter>();
    private readonly ISessionStoreWriter sessionStore = Substitute.For<ISessionStoreWriter>();
    private readonly IPairedDeviceStoreWriter pairedDeviceStore = Substitute.For<IPairedDeviceStoreWriter>();
    private readonly ISynchronizationClientService syncClient = Substitute.For<ISynchronizationClientService>();
    private readonly IPairingClientCoordinator pairing = Substitute.For<IPairingClientCoordinator>();

    private SyncCoordinator CreateCoordinator(
        ISynchronizationClientService? syncClientOverride = null,
        IPairingClientCoordinator? pairingOverride = null) =>
        new(
            bikeStore,
            setupStore,
            sessionStore,
            pairedDeviceStore,
            syncClientOverride ?? syncClient,
            pairingOverride ?? pairing);

    // ----- CanSync -----

    [Fact]
    public void CanSync_IsFalse_WhenPairingReportsNotPaired()
    {
        pairing.IsPaired.Returns(false);

        var coordinator = CreateCoordinator();

        Assert.False(coordinator.CanSync);
        Assert.False(coordinator.IsPaired);
    }

    [Fact]
    public async Task CanSync_IsFalse_WhileSyncIsRunning()
    {
        pairing.IsPaired.Returns(true);
        var gate = new TaskCompletionSource();
        syncClient.SyncAll().Returns(gate.Task);

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
        pairing.IsPaired.Returns(false);
        var coordinator = CreateCoordinator();

        await coordinator.SyncAllAsync();

        await syncClient.DidNotReceive().SyncAll();
        await bikeStore.DidNotReceive().RefreshAsync();
    }

    [Fact]
    public async Task SyncAllAsync_IsNoOp_WhenSynchronizationClientServiceIsNull()
    {
        pairing.IsPaired.Returns(true);
        // Bypass the helper — it uses `??` to fall back to the class-level
        // substitute, so a null override there wouldn't actually inject null.
        var coordinator = new SyncCoordinator(
            bikeStore, setupStore, sessionStore, pairedDeviceStore,
            synchronizationClientService: null,
            pairingClientCoordinator: pairing);

        await coordinator.SyncAllAsync();

        await bikeStore.DidNotReceive().RefreshAsync();
        await setupStore.DidNotReceive().RefreshAsync();
        await sessionStore.DidNotReceive().RefreshAsync();
        await pairedDeviceStore.DidNotReceive().RefreshAsync();
    }

    // ----- SyncAllAsync happy path -----

    [Fact]
    public async Task SyncAllAsync_CallsSyncAllAndRefreshesStoresInOrder()
    {
        pairing.IsPaired.Returns(true);
        var callOrder = new List<string>();
        syncClient.SyncAll().Returns(_ =>
        {
            callOrder.Add(nameof(ISynchronizationClientService.SyncAll));
            return Task.CompletedTask;
        });
        bikeStore.RefreshAsync().Returns(_ =>
        {
            callOrder.Add("bike");
            return Task.CompletedTask;
        });
        setupStore.RefreshAsync().Returns(_ =>
        {
            callOrder.Add("setup");
            return Task.CompletedTask;
        });
        sessionStore.RefreshAsync().Returns(_ =>
        {
            callOrder.Add("session");
            return Task.CompletedTask;
        });
        pairedDeviceStore.RefreshAsync().Returns(_ =>
        {
            callOrder.Add("pairedDevice");
            return Task.CompletedTask;
        });

        var coordinator = CreateCoordinator();
        await coordinator.SyncAllAsync();

        Assert.Equal(
            new[] { nameof(ISynchronizationClientService.SyncAll), "bike", "setup", "session", "pairedDevice" },
            callOrder);
    }

    [Fact]
    public async Task SyncAllAsync_TogglesIsRunning_AndRaisesEvents()
    {
        pairing.IsPaired.Returns(true);
        var coordinator = CreateCoordinator();

        var isRunningChanged = 0;
        var canSyncChanged = 0;
        coordinator.IsRunningChanged += (_, _) => isRunningChanged++;
        coordinator.CanSyncChanged += (_, _) => canSyncChanged++;

        await coordinator.SyncAllAsync();

        Assert.False(coordinator.IsRunning);
        // IsRunning flips true then false; each flip raises both events.
        Assert.Equal(2, isRunningChanged);
        Assert.Equal(2, canSyncChanged);
    }

    [Fact]
    public async Task SyncAllAsync_RaisesSyncCompleted_OnSuccess()
    {
        pairing.IsPaired.Returns(true);
        var coordinator = CreateCoordinator();

        var completed = 0;
        var failed = 0;
        coordinator.SyncCompleted += (_, _) => completed++;
        coordinator.SyncFailed += (_, _) => failed++;

        await coordinator.SyncAllAsync();

        Assert.Equal(1, completed);
        Assert.Equal(0, failed);
    }

    // ----- SyncAllAsync failure -----

    [Fact]
    public async Task SyncAllAsync_RaisesSyncFailed_AndResetsIsRunning_WhenSyncThrows()
    {
        pairing.IsPaired.Returns(true);
        syncClient.SyncAll().ThrowsAsync(new InvalidOperationException("boom"));
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
        await bikeStore.DidNotReceive().RefreshAsync();
    }

    // ----- Pairing event forwarding -----

    [Fact]
    public void PairingIsPairedChanged_ReRaisesIsPairedChanged_AndCanSyncChanged()
    {
        pairing.IsPaired.Returns(false);
        var coordinator = CreateCoordinator();

        var isPairedChanged = 0;
        var canSyncChanged = 0;
        coordinator.IsPairedChanged += (_, _) => isPairedChanged++;
        coordinator.CanSyncChanged += (_, _) => canSyncChanged++;

        pairing.IsPairedChanged += Raise.Event();

        Assert.Equal(1, isPairedChanged);
        Assert.Equal(1, canSyncChanged);
    }

    [Fact]
    public async Task PairingConfirmed_KicksOffSyncAllAsync_Automatically()
    {
        // Pre-seed IsPaired = true so the fire-and-forget SyncAllAsync's
        // CanSync check passes.
        pairing.IsPaired.Returns(true);
        var coordinator = CreateCoordinator();

        var syncCompleted = new TaskCompletionSource();
        coordinator.SyncCompleted += (_, _) => syncCompleted.TrySetResult();

        pairing.PairingConfirmed += Raise.Event();

        // Wait for the detached SyncAllAsync task to drive SyncCompleted.
        await syncCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await syncClient.Received(1).SyncAll();
        await bikeStore.Received(1).RefreshAsync();
        await setupStore.Received(1).RefreshAsync();
        await sessionStore.Received(1).RefreshAsync();
        await pairedDeviceStore.Received(1).RefreshAsync();
    }

    [Fact]
    public void PairingConfirmed_DoesNotSync_WhenCanSyncIsFalse()
    {
        // IsPaired is false by default — the auto-sync silently no-ops.
        pairing.IsPaired.Returns(false);
        var coordinator = CreateCoordinator();

        pairing.PairingConfirmed += Raise.Event();

        _ = syncClient.DidNotReceive().SyncAll();
    }

    // ----- Constructor tolerates null pairing -----

    [Fact]
    public void Constructor_DoesNotSubscribe_WhenPairingCoordinatorIsNull()
    {
        var coordinator = new SyncCoordinator(
            bikeStore, setupStore, sessionStore, pairedDeviceStore,
            synchronizationClientService: syncClient,
            pairingClientCoordinator: null);

        Assert.False(coordinator.IsPaired);
        Assert.False(coordinator.CanSync);
    }
}
