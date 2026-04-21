using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.Management;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Coordinators;

public class LiveDaqCoordinatorTests
{
    private readonly LiveDaqStore liveDaqStore = new();
    private readonly List<IReadOnlyCollection<LiveDaqSnapshot>> observedStates = [];
    private readonly Dictionary<string, LiveDaqSnapshot> currentObservedState = [];
    private readonly ILiveDaqKnownBoardsQuery knownBoardsQuery = Substitute.For<ILiveDaqKnownBoardsQuery>();
    private readonly ILiveDaqCatalogService catalogService = Substitute.For<ILiveDaqCatalogService>();
    private readonly ILiveDaqSharedStreamRegistry sharedStreamRegistry = Substitute.For<ILiveDaqSharedStreamRegistry>();
    private readonly ILiveDaqSharedStream sharedStream = Substitute.For<ILiveDaqSharedStream>();
    private readonly ILiveSessionServiceFactory liveSessionServiceFactory = Substitute.For<ILiveSessionServiceFactory>();
    private readonly ILiveSessionService liveSessionService = Substitute.For<ILiveSessionService>();
    private readonly ISessionCoordinator sessionCoordinator = Substitute.For<ISessionCoordinator>();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>();
    private readonly IDaqManagementService daqManagementService = Substitute.For<IDaqManagementService>();
    private readonly IFilesService filesService = Substitute.For<IFilesService>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly BehaviorSubject<IReadOnlyList<KnownLiveDaqRecord>> knownBoardsChanges = new([]);
    private readonly BehaviorSubject<IReadOnlyList<LiveDaqCatalogEntry>> catalogEntries = new([]);
    private readonly BehaviorSubject<LiveSessionPresentationSnapshot> liveSessionSnapshots = new(LiveSessionPresentationSnapshot.Empty);
    private readonly IDisposable browseLease = Substitute.For<IDisposable>();

    public LiveDaqCoordinatorTests()
    {
        liveDaqStore.Connect().Subscribe(RecordObservedState);
        knownBoardsQuery.Changes.Returns(knownBoardsChanges);
        catalogService.Observe().Returns(catalogEntries);
        catalogService.AcquireBrowse().Returns(browseLease);
        sharedStream.RequestedConfiguration.Returns(LiveDaqStreamConfiguration.Default);
        sharedStream.CurrentState.Returns(LiveDaqSharedStreamState.Empty);
        sharedStreamRegistry.GetOrCreate(Arg.Any<LiveDaqSnapshot>()).Returns(sharedStream);
        liveSessionService.Snapshots.Returns(liveSessionSnapshots);
        liveSessionService.GraphBatches.Returns(Observable.Empty<LiveGraphBatch>());
        liveSessionService.Current.Returns(LiveSessionPresentationSnapshot.Empty);
        liveSessionService.DisposeAsync().Returns(ValueTask.CompletedTask);
        liveSessionServiceFactory.Create(Arg.Any<LiveDaqSessionContext>(), Arg.Any<ILiveDaqSharedStream>())
            .Returns(liveSessionService);
    }

    private LiveDaqCoordinator CreateCoordinator() =>
        new(
            liveDaqStore,
            knownBoardsQuery,
            catalogService,
            sharedStreamRegistry,
            liveSessionServiceFactory,
            sessionCoordinator,
            tileLayerService,
            daqManagementService,
            filesService,
            shell,
            dialogService);

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
            new LiveDaqCatalogEntry(boardId.ToString(), boardId.ToString(), boardId.ToString(), "192.168.0.20", 5555),
            new LiveDaqCatalogEntry("192.168.0.21:6666", "192.168.0.21:6666", null, "192.168.0.21", 6666)
        ]);

        var knownSnapshot = liveDaqStore.Get(boardId.ToString());
        Assert.NotNull(knownSnapshot);
        Assert.True(knownSnapshot.IsOnline);
        Assert.Equal("192.168.0.20:5555", knownSnapshot.Endpoint);
        Assert.Equal("old setup", knownSnapshot.SetupName);
        Assert.Equal("old bike", knownSnapshot.BikeName);

        var discoveredOnly = liveDaqStore.Get("192.168.0.21:6666");
        Assert.NotNull(discoveredOnly);
        Assert.True(discoveredOnly.IsOnline);
        Assert.Null(discoveredOnly.SetupName);
        Assert.Null(discoveredOnly.BikeName);

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
            new LiveDaqCatalogEntry(boardId.ToString(), boardId.ToString(), boardId.ToString(), "192.168.0.20", 5555),
            new LiveDaqCatalogEntry("192.168.0.21:6666", "192.168.0.21:6666", null, "192.168.0.21", 6666)
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
            new LiveDaqCatalogEntry(firstBoard.ToString(), firstBoard.ToString(), firstBoard.ToString(), "192.168.0.20", 5555),
        ]);

        // Every published state must include both known boards — no observer
        // should ever witness an empty or partial catalog during reconcile.
        foreach (var state in observedStates)
        {
            Assert.Equal(2, state.Count);
        }
    }

    [Fact]
    public async Task SelectAsync_RoutesThroughOpenOrFocus_WithIdentityMatcher()
    {
        var snapshot = new LiveDaqSnapshot(
            IdentityKey: "board-1",
            DisplayName: "Board 1",
            BoardId: "board-1",
            Host: "192.168.0.30",
            Port: 7777,
            IsOnline: true,
            SetupName: "setup",
            BikeName: "bike");
        liveDaqStore.Upsert(snapshot);

        Func<LiveDaqDetailViewModel, bool>? capturedMatch = null;
        Func<LiveDaqDetailViewModel>? capturedCreate = null;
        shell.When(s => s.OpenOrFocus(
                Arg.Any<Func<LiveDaqDetailViewModel, bool>>(),
                Arg.Any<Func<LiveDaqDetailViewModel>>()))
            .Do(callInfo =>
            {
                capturedMatch = callInfo.ArgAt<Func<LiveDaqDetailViewModel, bool>>(0);
                capturedCreate = callInfo.ArgAt<Func<LiveDaqDetailViewModel>>(1);
            });

        await CreateCoordinator().SelectAsync(snapshot.IdentityKey);

        shell.Received(1).OpenOrFocus(
            Arg.Any<Func<LiveDaqDetailViewModel, bool>>(),
            Arg.Any<Func<LiveDaqDetailViewModel>>());

        Assert.NotNull(capturedCreate);
        var created = capturedCreate();
        Assert.Equal(snapshot.IdentityKey, created.IdentityKey);
        Assert.Equal(snapshot.DisplayName, created.Name);
        sharedStreamRegistry.Received(1).GetOrCreate(snapshot);
        Assert.NotNull(capturedMatch);
        Assert.True(capturedMatch(created));

        var other = new LiveDaqDetailViewModel(
            snapshot with { IdentityKey = "board-2", DisplayName = "Board 2", BoardId = "board-2" },
            sharedStream,
            Substitute.For<ILiveDaqCoordinator>(),
            daqManagementService,
            filesService,
            shell,
            dialogService,
            knownBoardsQuery,
            liveDaqStore);
        Assert.False(capturedMatch(other));
    }

    [Fact]
    public async Task OpenSessionAsync_RoutesThroughOpenOrFocus_WithIdentityMatcher()
    {
        var snapshot = new LiveDaqSnapshot(
            IdentityKey: "board-1",
            DisplayName: "Board 1",
            BoardId: "board-1",
            Host: "192.168.0.30",
            Port: 7777,
            IsOnline: true,
            SetupName: "setup",
            BikeName: "bike");
        liveDaqStore.Upsert(snapshot);
        knownBoardsQuery.GetSessionContext(snapshot.IdentityKey).Returns(CreateSessionContext(snapshot.IdentityKey, snapshot.DisplayName));

        Func<LiveSessionDetailViewModel, bool>? capturedMatch = null;
        Func<LiveSessionDetailViewModel>? capturedCreate = null;
        shell.When(s => s.OpenOrFocus(
                Arg.Any<Func<LiveSessionDetailViewModel, bool>>(),
                Arg.Any<Func<LiveSessionDetailViewModel>>()))
            .Do(callInfo =>
            {
                capturedMatch = callInfo.ArgAt<Func<LiveSessionDetailViewModel, bool>>(0);
                capturedCreate = callInfo.ArgAt<Func<LiveSessionDetailViewModel>>(1);
            });

        await CreateCoordinator().OpenSessionAsync(snapshot.IdentityKey);

        shell.Received(1).OpenOrFocus(
            Arg.Any<Func<LiveSessionDetailViewModel, bool>>(),
            Arg.Any<Func<LiveSessionDetailViewModel>>());

        Assert.NotNull(capturedCreate);
        var created = capturedCreate();
        Assert.Equal(snapshot.IdentityKey, created.IdentityKey);
        Assert.Equal("setup", created.SetupName);
        sharedStreamRegistry.Received(1).GetOrCreate(snapshot);
        Assert.NotNull(capturedMatch);
        Assert.True(capturedMatch(created));

        var other = new LiveSessionDetailViewModel(
            CreateSessionContext("board-2", "Board 2"),
            liveSessionService,
            sessionCoordinator,
            tileLayerService,
            shell,
            dialogService);
        Assert.False(capturedMatch(other));
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
            BikeData: new BikeData(63, 180, 170, measurement => measurement, measurement => measurement),
            TravelCalibration: new LiveDaqTravelCalibration(null, null));
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