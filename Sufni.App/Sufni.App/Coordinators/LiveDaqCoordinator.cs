using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;
using Serilog;

namespace Sufni.App.Coordinators;

// Reconciles discovery entries with known-board app data and routes row selection to
// identity-keyed live preview tabs.
public sealed class LiveDaqCoordinator : ILiveDaqCoordinator
{
    private static readonly ILogger logger = Log.ForContext<LiveDaqCoordinator>();

    private readonly ILiveDaqStoreWriter liveDaqStore;
    private readonly ILiveDaqKnownBoardsQuery knownBoardsQuery;
    private readonly ILiveDaqCatalogService liveDaqCatalogService;
    private readonly ILiveDaqSharedStreamRegistry liveDaqSharedStreamRegistry;
    private readonly ILiveSessionServiceFactory liveSessionServiceFactory;
    private readonly ISessionCoordinator sessionCoordinator;
    private readonly ITileLayerService tileLayerService;
    private readonly IShellCoordinator shell;
    private readonly IDialogService dialogService;

    private readonly object reconcileGate = new();
    private IReadOnlyDictionary<string, KnownLiveDaqRecord> knownBoards = new Dictionary<string, KnownLiveDaqRecord>();
    private IReadOnlyList<LiveDaqCatalogEntry> catalogEntries = [];
    private CompositeDisposable? activeSubscriptions;

    public LiveDaqCoordinator(
        ILiveDaqStoreWriter liveDaqStore,
        ILiveDaqKnownBoardsQuery knownBoardsQuery,
        ILiveDaqCatalogService liveDaqCatalogService,
        ILiveDaqSharedStreamRegistry liveDaqSharedStreamRegistry,
        ILiveSessionServiceFactory liveSessionServiceFactory,
        ISessionCoordinator sessionCoordinator,
        ITileLayerService tileLayerService,
        IShellCoordinator shell,
        IDialogService dialogService)
    {
        this.liveDaqStore = liveDaqStore;
        this.knownBoardsQuery = knownBoardsQuery;
        this.liveDaqCatalogService = liveDaqCatalogService;
        this.liveDaqSharedStreamRegistry = liveDaqSharedStreamRegistry;
        this.liveSessionServiceFactory = liveSessionServiceFactory;
        this.sessionCoordinator = sessionCoordinator;
        this.tileLayerService = tileLayerService;
        this.shell = shell;
        this.dialogService = dialogService;
    }

    public void Activate()
    {
        if (activeSubscriptions is not null)
        {
            return;
        }

        var subscriptions = new CompositeDisposable();
        activeSubscriptions = subscriptions;

        logger.Verbose(
            "Live DAQ browse activated with {KnownBoardCount} known boards",
            knownBoards.Count);

        subscriptions.Add(liveDaqCatalogService.AcquireBrowse());
        subscriptions.Add(knownBoardsQuery.Changes.Subscribe(records =>
        {
            lock (reconcileGate)
            {
                knownBoards = records.ToDictionary(r => r.IdentityKey, StringComparer.OrdinalIgnoreCase);
                ReconcileLocked();
            }
        }));
        subscriptions.Add(liveDaqCatalogService.Observe().Subscribe(entries =>
        {
            lock (reconcileGate)
            {
                catalogEntries = entries;
                ReconcileLocked();
            }
        }));
    }

    public void Deactivate()
    {
        var wasActive = activeSubscriptions is not null;
        activeSubscriptions?.Dispose();
        activeSubscriptions = null;

        if (wasActive)
        {
            logger.Verbose("Live DAQ browse deactivated");
        }

        lock (reconcileGate)
        {
            catalogEntries = [];
            ReconcileLocked();
        }
    }

    public Task SelectAsync(string identityKey)
    {
        var snapshot = liveDaqStore.Get(identityKey);
        if (snapshot is null)
        {
            logger.Verbose(
                "Live DAQ selection ignored because no snapshot was found for {IdentityKey}",
                identityKey);
            return Task.CompletedTask;
        }

        logger.Information(
            "Opening live DAQ detail for {IdentityKey} {BoardId} {Endpoint}",
            snapshot.IdentityKey,
            snapshot.BoardId,
            snapshot.Endpoint);

        shell.OpenOrFocus<LiveDaqDetailViewModel>(
            detail => detail.IdentityKey == snapshot.IdentityKey,
            () => new LiveDaqDetailViewModel(snapshot, liveDaqSharedStreamRegistry.GetOrCreate(snapshot), this, shell, dialogService, knownBoardsQuery, liveDaqStore));

        return Task.CompletedTask;
    }

    public Task OpenSessionAsync(string identityKey)
    {
        var snapshot = liveDaqStore.Get(identityKey);
        if (snapshot is null)
        {
            logger.Verbose(
                "Live session opening ignored because no snapshot was found for {IdentityKey}",
                identityKey);
            return Task.CompletedTask;
        }

        var context = knownBoardsQuery.GetSessionContext(identityKey);
        if (context is null)
        {
            logger.Verbose(
                "Live session opening ignored because no session context was found for {IdentityKey}",
                identityKey);
            return Task.CompletedTask;
        }

        logger.Information(
            "Opening live session detail for {IdentityKey} {BoardId} {Endpoint}",
            snapshot.IdentityKey,
            snapshot.BoardId,
            snapshot.Endpoint);

        shell.OpenOrFocus<LiveSessionDetailViewModel>(
            detail => detail.IdentityKey == snapshot.IdentityKey,
            () => new LiveSessionDetailViewModel(
                context,
                liveSessionServiceFactory.Create(context, liveDaqSharedStreamRegistry.GetOrCreate(snapshot)),
                sessionCoordinator,
                tileLayerService,
                shell,
                dialogService));

        return Task.CompletedTask;
    }

    // Reconciles the live DAQ snapshot state by merging known boards with current catalog entries
    // and publishes the result as one atomic store update so subscribers never see a transient
    // empty catalog. The caller must already hold reconcileGate; the store write stays inside
    // that critical section so two concurrent reconciles cannot publish their snapshots out of
    // order.
    private void ReconcileLocked()
    {
        var snapshots = new Dictionary<string, LiveDaqSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var knownBoard in knownBoards.Values)
        {
            snapshots[knownBoard.IdentityKey] = new LiveDaqSnapshot(
                IdentityKey: knownBoard.IdentityKey,
                DisplayName: knownBoard.DisplayName,
                BoardId: knownBoard.BoardId,
                Host: null,
                Port: null,
                IsOnline: false,
                SetupName: knownBoard.SetupName,
                BikeName: knownBoard.BikeName);
        }

        foreach (var entry in catalogEntries)
        {
            var merged = knownBoards.TryGetValue(entry.IdentityKey, out var knownBoard)
                ? new LiveDaqSnapshot(
                    IdentityKey: entry.IdentityKey,
                    DisplayName: knownBoard.DisplayName,
                    BoardId: entry.BoardId,
                    Host: entry.Host,
                    Port: entry.Port,
                    IsOnline: true,
                    SetupName: knownBoard.SetupName,
                    BikeName: knownBoard.BikeName)
                : new LiveDaqSnapshot(
                    IdentityKey: entry.IdentityKey,
                    DisplayName: entry.DisplayName,
                    BoardId: entry.BoardId,
                    Host: entry.Host,
                    Port: entry.Port,
                    IsOnline: true,
                    SetupName: null,
                    BikeName: null);

            snapshots[entry.IdentityKey] = merged;
        }

        var ordered = snapshots.Values
            .OrderBy(snapshot => snapshot.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var knownBoardCount = knownBoards.Count;
        var catalogEntryCount = catalogEntries.Count;
        var onlineCount = ordered.Count(snapshot => snapshot.IsOnline);

        liveDaqStore.ReplaceAll(ordered);

        logger.Verbose(
            "Live DAQ catalog reconciled with {KnownBoardCount} known boards, {CatalogEntryCount} catalog entries, {PublishedCount} published rows, {OnlineCount} online rows, and {OfflineCount} offline rows",
            knownBoardCount,
            catalogEntryCount,
            ordered.Length,
            onlineCount,
            ordered.Length - onlineCount);
    }
}