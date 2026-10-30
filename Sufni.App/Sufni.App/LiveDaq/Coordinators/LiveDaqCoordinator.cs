using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Serilog;

using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Stores;
namespace Sufni.App.LiveDaq.Coordinators;

// Reconciles discovery presence with known boards.
public class LiveDaqCoordinator : ILiveDaqCoordinator
{
    private static readonly ILogger logger = Log.ForContext<LiveDaqCoordinator>();

    private readonly ILiveDaqStoreWriter liveDaqStore;
    private readonly ILiveDaqKnownBoardsQuery knownBoardsQuery;
    private readonly ILiveDaqCatalogService liveDaqCatalogService;

    private readonly System.Threading.Lock reconcileGate = new();
    private IReadOnlyDictionary<string, KnownLiveDaqRecord> knownBoards = new Dictionary<string, KnownLiveDaqRecord>();
    private IReadOnlyList<LiveDaqCatalogEntry> catalogEntries = [];
    private CompositeDisposable? activeSubscriptions;

    public LiveDaqCoordinator(
        ILiveDaqStoreWriter liveDaqStore,
        ILiveDaqKnownBoardsQuery knownBoardsQuery,
        ILiveDaqCatalogService liveDaqCatalogService)
    {
        this.liveDaqStore = liveDaqStore;
        this.knownBoardsQuery = knownBoardsQuery;
        this.liveDaqCatalogService = liveDaqCatalogService;
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
                BikeName: knownBoard.BikeName,
                ProtocolVersion: LiveProtocolVersion.V2);
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
                    BikeName: knownBoard.BikeName,
                    ProtocolVersion: entry.ProtocolVersion)
                : new LiveDaqSnapshot(
                    IdentityKey: entry.IdentityKey,
                    DisplayName: entry.DisplayName,
                    BoardId: entry.BoardId,
                    Host: entry.Host,
                    Port: entry.Port,
                    IsOnline: true,
                    SetupName: null,
                    BikeName: null,
                    ProtocolVersion: entry.ProtocolVersion);

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
