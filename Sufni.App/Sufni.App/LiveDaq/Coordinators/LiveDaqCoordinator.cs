using Sufni.App.Acquisition.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using Serilog;
using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Stores;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.LiveDaq.Coordinators;

// Owns live DAQ presence reconciliation and capture-save workflows.
public partial class LiveDaqCoordinator : ILiveDaqCoordinator, IAsyncDisposable
{
    private static readonly ILogger logger = Log.ForContext<LiveDaqCoordinator>();

    private readonly IDaqManagementService managementService;
    private readonly DeviceConfigFileService configFiles;
    private readonly ILiveDaqSharedStreamRegistry streamRegistry;
    private readonly ILiveDaqStoreWriter liveDaqStore;
    private readonly ILiveDaqKnownBoardsQuery knownBoardsQuery;
    private readonly ILiveDaqCatalogService liveDaqCatalogService;

    private readonly ISessionStoreWriter sessionStore;
    private readonly ISessionRepository sessionRepository;
    private readonly ITrackRepository trackQueries;
    private readonly ISynchronizableRepository<Setup> setupRepository;
    private readonly ISynchronizableRepository<Bike> bikeRepository;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly IRecordedSessionReprocessor recordedSessionReprocessor;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ISessionPreferences sessionPreferences;
    private readonly IUiThreadDispatcher dispatcher;
    private readonly StorePublication publication;
    private readonly ITrackStoreWriter trackStore;

    private readonly System.Threading.Lock reconcileGate = new();
    private IReadOnlyDictionary<string, KnownLiveDaqRecord> knownBoards = new Dictionary<string, KnownLiveDaqRecord>();
    private IReadOnlyList<LiveDaqCatalogEntry> catalogEntries = [];
    private CompositeDisposable? activeSubscriptions;

    public LiveDaqCoordinator(
        ILiveDaqStoreWriter liveDaqStore,
        ILiveDaqKnownBoardsQuery knownBoardsQuery,
        ILiveDaqCatalogService liveDaqCatalogService,
        ISessionStoreWriter sessionStore,
        ISessionRepository sessionRepository,
        ITrackRepository trackQueries,
        ISynchronizableRepository<Setup> setupRepository,
        ISynchronizableRepository<Bike> bikeRepository,
        IRecordedSessionSourceStoreWriter sourceStore,
        IRecordedSessionReprocessor recordedSessionReprocessor,
        IBackgroundTaskRunner backgroundTaskRunner,
        ISessionPreferences sessionPreferences,
        IUiThreadDispatcher dispatcher,
        StorePublication publication,
        ITrackStoreWriter trackStore,
        IDaqManagementService managementService,
        DeviceConfigFileService configFiles,
        ILiveDaqSharedStreamRegistry streamRegistry)
    {
        this.liveDaqStore = liveDaqStore;
        this.knownBoardsQuery = knownBoardsQuery;
        this.liveDaqCatalogService = liveDaqCatalogService;
        this.sessionStore = sessionStore;
        this.sessionRepository = sessionRepository;
        this.trackQueries = trackQueries;
        this.setupRepository = setupRepository;
        this.bikeRepository = bikeRepository;
        this.sourceStore = sourceStore;
        this.recordedSessionReprocessor = recordedSessionReprocessor;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.sessionPreferences = sessionPreferences;
        this.dispatcher = dispatcher;
        this.publication = publication;
        this.trackStore = trackStore;
        this.managementService = managementService;
        this.configFiles = configFiles;
        this.streamRegistry = streamRegistry;
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
