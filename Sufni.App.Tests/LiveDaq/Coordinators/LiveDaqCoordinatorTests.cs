using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Sessions.Store;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using NSubstitute;
using Sufni.Telemetry;
using Sufni.App.Bikes.Models;

using Sufni.App.Acquisition.Services;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Coordinators;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Stores;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Services;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Extensions;

namespace Sufni.App.Tests.LiveDaq.Coordinators;

public partial class LiveDaqCoordinatorTests
{
    private readonly LiveDaqStore liveDaqStore = new(new InlineUiThreadDispatcher());
    private readonly List<IReadOnlyCollection<LiveDaqSnapshot>> observedStates = [];
    private readonly Dictionary<string, LiveDaqSnapshot> currentObservedState = [];
    private readonly ILiveDaqKnownBoardsQuery knownBoardsQuery = Substitute.For<ILiveDaqKnownBoardsQuery>();
    private readonly ILiveDaqCatalogService catalogService = Substitute.For<ILiveDaqCatalogService>();
    private readonly BehaviorSubject<IReadOnlyList<KnownLiveDaqRecord>> knownBoardsChanges = new([]);
    private readonly BehaviorSubject<IReadOnlyList<LiveDaqCatalogEntry>> catalogEntries = new([]);
    private readonly IDaqManagementService managementService = Substitute.For<IDaqManagementService>();
    private readonly IFilesService filesService = Substitute.For<IFilesService>();
    private readonly ILiveDaqSharedStreamRegistry streamRegistry = Substitute.For<ILiveDaqSharedStreamRegistry>();
    private readonly IDisposable browseLease = Substitute.For<IDisposable>();

    public LiveDaqCoordinatorTests()
    {
        liveDaqStore.Connect().Subscribe(RecordObservedState);
        knownBoardsQuery.Changes.Returns(knownBoardsChanges);
        catalogService.Observe().Returns(catalogEntries);
        catalogService.Get(Arg.Any<string>()).Returns(call => catalogEntries.Value.FirstOrDefault(entry => entry.IdentityKey == call.Arg<string>()));
        streamRegistry.ObserveTransport(Arg.Any<string>()).Returns(Observable.Return<DaqTransportHolder?>(null));
        catalogService.AcquireBrowse().Returns(browseLease);

    }

    private LiveDaqCoordinator CreateCoordinator(ISessionStoreWriter? store = null, ISessionRepository? repository = null,
        IRecordedSessionSourceStoreWriter? sources = null, ITrackStoreWriter? tracks = null) =>
        new(
            liveDaqStore,
            knownBoardsQuery,
            catalogService, store ?? sessionStore, repository ?? sessionRepository,
            trackQueries, setupRepository, bikeRepository, sources ?? sourceStore, reprocessor,
            backgroundTaskRunner, sessionPreferences, new InlineUiThreadDispatcher(), publication, tracks ?? trackStore, managementService, filesService, streamRegistry);

    [Fact]
    public void Activate_SeedsOfflineKnownBoards_AndAcquiresBrowse()
    {
        var boardId = Guid.NewGuid();
        knownBoardsChanges.OnNext(
        [
            new KnownLiveDaqRecord(
                IdentityKey: boardId.ToString(),
                DisplayName: boardId.ToString(),
                BoardId: boardId.ToString(),
                SetupId: Guid.NewGuid(),
                SetupName: "park setup",
                BikeId: Guid.NewGuid(),
                BikeName: "demo bike")
        ]);

        CreateCoordinator().Activate();

        catalogService.Received(1).AcquireBrowse();
        var snapshot = Assert.Single(observedStates[^1]);
        Assert.Equal(boardId.ToString(), snapshot.IdentityKey);
        Assert.False(snapshot.IsOnline);
        Assert.Equal("park setup", snapshot.SetupName);
        Assert.Equal("demo bike", snapshot.BikeName);
        Assert.Null(snapshot.Endpoint);
        Assert.Equal(LiveProtocolVersion.V2, snapshot.ProtocolVersion);
    }

    [Fact]
    public void Activate_MergesDiscoveredEntries_AndRefreshesEnrichment_OnQueryChange()
    {
        var boardId = Guid.NewGuid();
        knownBoardsChanges.OnNext(
        [
            new KnownLiveDaqRecord(
                IdentityKey: boardId.ToString(),
                DisplayName: boardId.ToString(),
                BoardId: boardId.ToString(),
                SetupId: Guid.NewGuid(),
                SetupName: "old setup",
                BikeId: Guid.NewGuid(),
                BikeName: "old bike")
        ]);

        var coordinator = CreateCoordinator();
        coordinator.Activate();

        catalogEntries.OnNext(
        [
            new LiveDaqCatalogEntry(
                boardId.ToString(),
                boardId.ToString(),
                boardId.ToString(), new ServiceEndpoint("192.168.0.20", 5555),
                LiveProtocolVersion.V3),
            new LiveDaqCatalogEntry("192.168.0.21:6666", "192.168.0.21:6666", null, new ServiceEndpoint("192.168.0.21", 6666))
        ]);

        var knownSnapshot = liveDaqStore.Get(boardId.ToString());
        Assert.NotNull(knownSnapshot);
        Assert.True(knownSnapshot.IsOnline);
        Assert.Equal("192.168.0.20:5555", knownSnapshot.Endpoint);
        Assert.Equal("old setup", knownSnapshot.SetupName);
        Assert.Equal("old bike", knownSnapshot.BikeName);
        Assert.Equal(LiveProtocolVersion.V3, knownSnapshot.ProtocolVersion);

        var discoveredOnly = liveDaqStore.Get("192.168.0.21:6666");
        Assert.NotNull(discoveredOnly);
        Assert.True(discoveredOnly.IsOnline);
        Assert.Null(discoveredOnly.SetupName);
        Assert.Null(discoveredOnly.BikeName);
        Assert.Equal(LiveProtocolVersion.V2, discoveredOnly.ProtocolVersion);

        knownBoardsChanges.OnNext(
        [
            new KnownLiveDaqRecord(
                IdentityKey: boardId.ToString(),
                DisplayName: boardId.ToString(),
                BoardId: boardId.ToString(),
                SetupId: Guid.NewGuid(),
                SetupName: "new setup",
                BikeId: Guid.NewGuid(),
                BikeName: "new bike")
        ]);

        knownSnapshot = liveDaqStore.Get(boardId.ToString());
        Assert.NotNull(knownSnapshot);
        Assert.True(knownSnapshot.IsOnline);
        Assert.Equal("new setup", knownSnapshot.SetupName);
        Assert.Equal("new bike", knownSnapshot.BikeName);
        Assert.Equal("192.168.0.20:5555", knownSnapshot.Endpoint);
        Assert.Equal(LiveProtocolVersion.V3, knownSnapshot.ProtocolVersion);
    }

    [Fact]
    public void Deactivate_DisposesBrowseLease_AndLeavesOnlyOfflineKnownBoards()
    {
        var boardId = Guid.NewGuid();
        knownBoardsChanges.OnNext(
        [
            new KnownLiveDaqRecord(
                IdentityKey: boardId.ToString(),
                DisplayName: boardId.ToString(),
                BoardId: boardId.ToString(),
                SetupId: null,
                SetupName: null,
                BikeId: null,
                BikeName: null)
        ]);

        var coordinator = CreateCoordinator();
        coordinator.Activate();
        catalogEntries.OnNext(
        [
            new LiveDaqCatalogEntry(boardId.ToString(), boardId.ToString(), boardId.ToString(), new ServiceEndpoint("192.168.0.20", 5555)),
            new LiveDaqCatalogEntry("192.168.0.21:6666", "192.168.0.21:6666", null, new ServiceEndpoint("192.168.0.21", 6666))
        ]);

        coordinator.Deactivate();

        browseLease.Received(1).Dispose();
        var knownSnapshot = liveDaqStore.Get(boardId.ToString());
        Assert.NotNull(knownSnapshot);
        Assert.False(knownSnapshot.IsOnline);
        Assert.Null(knownSnapshot.Endpoint);
        Assert.Null(liveDaqStore.Get("192.168.0.21:6666"));
        Assert.Single(observedStates[^1]);
    }

    [Fact]
    public void CatalogRemovals_LeaveKnownBoardsOffline_AndRemoveUnknownBoards()
    {
        const string knownId = "known";
        knownBoardsChanges.OnNext([new KnownLiveDaqRecord(knownId, "ride", knownId, null, null, null, null)]);
        var coordinator = CreateCoordinator();
        coordinator.Activate();
        catalogEntries.OnNext([
            new LiveDaqCatalogEntry(knownId, "ride", knownId, new ServiceEndpoint("192.168.0.10", 1557)),
            new LiveDaqCatalogEntry("unknown", "new", null, new ServiceEndpoint("192.168.0.11", 1557))]);
        Assert.True(liveDaqStore.Get(knownId)!.IsOnline);

        catalogEntries.OnNext([]);

        Assert.False(liveDaqStore.Get(knownId)!.IsOnline);
        Assert.Null(liveDaqStore.Get(knownId)!.Endpoint);
        Assert.Null(liveDaqStore.Get("unknown"));
        coordinator.Deactivate();
    }

    [Fact]
    public void Reconcile_PublishesAtomicUpdate_WithoutTransientEmptyState()
    {
        var firstBoard = Guid.NewGuid();
        var secondBoard = Guid.NewGuid();
        knownBoardsChanges.OnNext(
        [
            new KnownLiveDaqRecord(
                IdentityKey: firstBoard.ToString(),
                DisplayName: firstBoard.ToString(),
                BoardId: firstBoard.ToString(),
                SetupId: Guid.NewGuid(),
                SetupName: "first",
                BikeId: Guid.NewGuid(),
                BikeName: "bike"),
            new KnownLiveDaqRecord(
                IdentityKey: secondBoard.ToString(),
                DisplayName: secondBoard.ToString(),
                BoardId: secondBoard.ToString(),
                SetupId: Guid.NewGuid(),
                SetupName: "second",
                BikeId: Guid.NewGuid(),
                BikeName: "bike"),
        ]);

        var coordinator = CreateCoordinator();
        coordinator.Activate();

        catalogEntries.OnNext(
        [
            new LiveDaqCatalogEntry(firstBoard.ToString(), firstBoard.ToString(), firstBoard.ToString(), new ServiceEndpoint("192.168.0.20", 5555)),
        ]);

        // Every published state must include both known boards — no observer
        // should ever witness an empty or partial catalog during reconcile.
        foreach (var state in observedStates)
        {
            Assert.Equal(2, state.Count);
        }
    }

    private static LiveDaqSessionContext CreateSessionContext(string identityKey, string displayName)
    {
        return new LiveDaqSessionContext(
            IdentityKey: identityKey,
            BoardId: Guid.NewGuid(),
            DisplayName: displayName,
            SetupId: Guid.NewGuid(),
            SetupName: "setup",
            BikeId: Guid.NewGuid(),
            BikeName: "bike",
            BikeData: new BikeData(180, 170, measurement => measurement, measurement => measurement),
            TravelCalibration: new LiveDaqTravelCalibration(null, null),
            DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
            DampingSpeedCutoffOwner: new DampingSpeedCutoffOwner(Guid.Empty, 0));
    }

    private void RecordObservedState(IChangeSet<LiveDaqSnapshot, string> changes)
    {
        if (changes.Count == 0)
        {
            return;
        }

        foreach (var change in changes)
        {
            switch (change.Reason)
            {
                case ChangeReason.Remove:
                    currentObservedState.Remove(change.Key);
                    break;

                default:
                    currentObservedState[change.Key] = change.Current;
                    break;
            }
        }

        observedStates.Add(currentObservedState.Values.ToList());
    }
}
