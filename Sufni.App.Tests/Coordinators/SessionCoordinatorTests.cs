using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Coordinators;

public class SessionCoordinatorTests
{
    private readonly ISessionStoreWriter sessionStore = Substitute.For<ISessionStoreWriter>();
    private readonly IDatabaseService database = Substitute.For<IDatabaseService>();
    private readonly IHttpApiService http = Substitute.For<IHttpApiService>();
    private readonly ITrackCoordinator trackCoordinator = Substitute.For<ITrackCoordinator>();
    private readonly ISessionPresentationService sessionPresentationService = Substitute.For<ISessionPresentationService>();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = new InlineBackgroundTaskRunner();

    public SessionCoordinatorTests()
    {
        tileLayerService.AvailableLayers.Returns([]);
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);
    }

    private SessionCoordinator CreateCoordinator(ISynchronizationServerService? sync = null) =>
        new(sessionStore, database, http, backgroundTaskRunner, trackCoordinator, sessionPresentationService, tileLayerService, shell, dialogService, sync);

    // ----- OpenEditAsync -----

    [Fact]
    public async Task OpenEditAsync_NoOp_WhenSnapshotMissing()
    {
        sessionStore.Get(Arg.Any<Guid>()).Returns((SessionSnapshot?)null);

        await CreateCoordinator().OpenEditAsync(Guid.NewGuid());

        shell.DidNotReceiveWithAnyArgs().OpenOrFocus<SessionDetailViewModel>(default!, default!);
    }

    // OpenEditAsync's happy path constructs a SessionDetailViewModel,
    // which subscribes to NotesPage property-changed events on the
    // dispatcher thread — that needs the headless app, so it lives in
    // a separate [AvaloniaFact] below.
    [AvaloniaFact]
    public async Task OpenEditAsync_RoutesThroughOpenOrFocus_WhenSnapshotPresent()
    {
        var snapshot = TestSnapshots.Session();
        sessionStore.Get(snapshot.Id).Returns(snapshot);

        await CreateCoordinator().OpenEditAsync(snapshot.Id);

        shell.Received(1).OpenOrFocus(
            Arg.Any<Func<SessionDetailViewModel, bool>>(),
            Arg.Any<Func<SessionDetailViewModel>>());
    }

    // ----- SaveAsync -----

    [Fact]
    public async Task SaveAsync_HappyPath_WritesAndRefetchesAndUpserts()
    {
        var existing = TestSnapshots.Session(updated: 5);
        sessionStore.Get(existing.Id).Returns(existing);

        var session = new Session(existing.Id, "renamed", "", null) { Updated = 7 };
        var fresh = new Session(existing.Id, "renamed", "", null)
        {
            Updated = 7,
            HasProcessedData = true,
        };
        database.GetSessionAsync(existing.Id).Returns(fresh);

        var result = await CreateCoordinator().SaveAsync(session, baselineUpdated: 5);

        await database.Received(1).PutSessionAsync(session);
        await database.Received(1).GetSessionAsync(existing.Id);
        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(s =>
            s.Id == existing.Id && s.Name == "renamed" && s.Updated == 7 && s.HasProcessedData));
        var saved = Assert.IsType<SessionSaveResult.Saved>(result);
        Assert.Equal(7, saved.NewBaselineUpdated);
    }

    [Fact]
    public async Task SaveAsync_ReturnsFailed_WhenRefetchReturnsNull()
    {
        var existing = TestSnapshots.Session(updated: 5);
        sessionStore.Get(existing.Id).Returns(existing);
        database.GetSessionAsync(existing.Id).Returns((Session?)null);

        var session = new Session(existing.Id, "renamed", "", null);

        var result = await CreateCoordinator().SaveAsync(session, baselineUpdated: 5);

        Assert.IsType<SessionSaveResult.Failed>(result);
        sessionStore.DidNotReceive().Upsert(Arg.Any<SessionSnapshot>());
    }

    [Fact]
    public async Task SaveAsync_ReturnsConflict_WhenStoreIsNewer()
    {
        var current = TestSnapshots.Session(updated: 10);
        sessionStore.Get(current.Id).Returns(current);

        var session = new Session(current.Id, "stale", "", null);

        var result = await CreateCoordinator().SaveAsync(session, baselineUpdated: 5);

        var conflict = Assert.IsType<SessionSaveResult.Conflict>(result);
        Assert.Same(current, conflict.CurrentSnapshot);
        await database.DidNotReceive().PutSessionAsync(Arg.Any<Session>());
        sessionStore.DidNotReceive().Upsert(Arg.Any<SessionSnapshot>());
    }

    [Fact]
    public async Task SaveAsync_ReturnsFailed_WhenPutSessionThrows()
    {
        var existing = TestSnapshots.Session(updated: 5);
        sessionStore.Get(existing.Id).Returns(existing);
        database.PutSessionAsync(Arg.Any<Session>()).ThrowsAsync(new InvalidOperationException("disk full"));

        var session = new Session(existing.Id, "x", "", null);

        var result = await CreateCoordinator().SaveAsync(session, baselineUpdated: 5);

        Assert.IsType<SessionSaveResult.Failed>(result);
        sessionStore.DidNotReceive().Upsert(Arg.Any<SessionSnapshot>());
    }

    // ----- DeleteAsync -----

    [Fact]
    public async Task DeleteAsync_HappyPath_DeletesClosesAndRemoves()
    {
        var id = Guid.NewGuid();

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.Equal(SessionDeleteOutcome.Deleted, result.Outcome);
        await database.Received(1).DeleteAsync<Session>(id);
        shell.Received(1).CloseIfOpen(Arg.Any<Func<SessionDetailViewModel, bool>>());
        sessionStore.Received(1).Remove(id);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFailed_WhenDatabaseDeleteThrows()
    {
        var id = Guid.NewGuid();
        database.DeleteAsync<Session>(id).ThrowsAsync(new InvalidOperationException("locked"));

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.Equal(SessionDeleteOutcome.Failed, result.Outcome);
        sessionStore.DidNotReceiveWithAnyArgs().Remove(default);
        shell.DidNotReceiveWithAnyArgs().CloseIfOpen<SessionDetailViewModel>(default!);
    }

    // ----- EnsureTelemetryDataAvailableAsync -----

    [Fact]
    public async Task EnsureTelemetryDataAvailableAsync_NoOp_WhenAlreadyHasProcessedData()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        sessionStore.Get(snapshot.Id).Returns(snapshot);

        await CreateCoordinator().EnsureTelemetryDataAvailableAsync(snapshot.Id);

        await http.DidNotReceive().GetSessionPsstAsync(Arg.Any<Guid>());
        await database.DidNotReceive().PatchSessionPsstAsync(Arg.Any<Guid>(), Arg.Any<byte[]>());
    }

    [Fact]
    public async Task EnsureTelemetryDataAvailableAsync_PullsAndPatches_AndUpsertsRefetchedSnapshot()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        var psst = new byte[] { 1, 2, 3 };
        http.GetSessionPsstAsync(snapshot.Id).Returns(psst);
        var fresh = new Session(snapshot.Id, snapshot.Name, "", null)
        {
            Updated = 9,
            HasProcessedData = true,
        };
        database.GetSessionAsync(snapshot.Id).Returns(fresh);

        await CreateCoordinator().EnsureTelemetryDataAvailableAsync(snapshot.Id);

        await http.Received(1).GetSessionPsstAsync(snapshot.Id);
        await database.Received(1).PatchSessionPsstAsync(snapshot.Id, psst);
        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(s =>
            s.Id == snapshot.Id && s.HasProcessedData && s.Updated == 9));
    }

    [Fact]
    public async Task EnsureTelemetryDataAvailableAsync_Throws_WhenHttpReturnsNull()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        http.GetSessionPsstAsync(snapshot.Id).Returns((byte[]?)null);

        await Assert.ThrowsAsync<Exception>(() =>
            CreateCoordinator().EnsureTelemetryDataAvailableAsync(snapshot.Id));
        await database.DidNotReceive().PatchSessionPsstAsync(Arg.Any<Guid>(), Arg.Any<byte[]>());
    }

    // ----- Desktop / Mobile load workflows -----

    [Fact]
    public async Task LoadDesktopDetailAsync_ReturnsLoaded_WhenTelemetryPresent()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.Create();
        var percentages = new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8);
        var trackData = new SessionTrackPresentationData(
            Guid.NewGuid(),
            [new TrackPoint(1, 1, 1, 0)],
            [new TrackPoint(2, 2, 2, 0)],
            400.0);

        sessionStore.Get(snapshot.Id).Returns(snapshot);
        database.GetSessionPsstAsync(snapshot.Id).Returns(telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(trackData);
        sessionPresentationService.CalculateDamperPercentages(telemetry).Returns(percentages);

        var result = await CreateCoordinator().LoadDesktopDetailAsync(snapshot.Id);

        var loaded = Assert.IsType<SessionDesktopLoadResult.Loaded>(result);
        Assert.Same(telemetry, loaded.Data.TelemetryData);
        Assert.Same(trackData.TrackPoints, loaded.Data.TrackPoints);
        Assert.Equal(400.0, loaded.Data.MapVideoWidth);
        Assert.Equal(percentages, loaded.Data.DamperPercentages);
    }

    [Fact]
    public async Task LoadDesktopDetailAsync_ReturnsTelemetryPending_WhenTelemetryMissing()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        database.GetSessionPsstAsync(snapshot.Id).Returns(Task.FromResult<TelemetryData?>(null));

        var result = await CreateCoordinator().LoadDesktopDetailAsync(snapshot.Id);

        Assert.IsType<SessionDesktopLoadResult.TelemetryPending>(result);
        await trackCoordinator.DidNotReceive().LoadSessionTrackAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<TelemetryData>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadDesktopDetailAsync_ReturnsFailed_WhenSnapshotClaimsTelemetryButBlobMissing()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        database.GetSessionPsstAsync(snapshot.Id).Returns(Task.FromResult<TelemetryData?>(null));

        var result = await CreateCoordinator().LoadDesktopDetailAsync(snapshot.Id);

        Assert.IsType<SessionDesktopLoadResult.Failed>(result);
    }

    [Fact]
    public async Task LoadDesktopDetailAsync_ReturnsFailed_WhenTrackCoordinatorThrows()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.Create();
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        database.GetSessionPsstAsync(snapshot.Id).Returns(telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("track failed"));

        var result = await CreateCoordinator().LoadDesktopDetailAsync(snapshot.Id);

        Assert.IsType<SessionDesktopLoadResult.Failed>(result);
    }

    [Fact]
    public async Task LoadMobileDetailAsync_ReturnsCacheHit_WhenCacheExists()
    {
        var sessionId = Guid.NewGuid();
        var cache = new SessionCache { SessionId = sessionId, FrontTravelHistogram = "cached" };
        database.GetSessionCacheAsync(sessionId).Returns(cache);

        var result = await CreateCoordinator().LoadMobileDetailAsync(sessionId, new SessionPresentationDimensions(320, 180));

        var loaded = Assert.IsType<SessionMobileLoadResult.LoadedFromCache>(result);
        Assert.Equal("cached", loaded.Data.FrontTravelHistogram);
        await http.DidNotReceive().GetSessionPsstAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task LoadMobileDetailAsync_BuildsAndPersistsCache_WhenCacheMissing()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.Create();
        var cacheData = new SessionCachePresentationData(
            "front-travel",
            null,
            "front-velocity",
            null,
            null,
            null,
            new SessionDamperPercentages(1, null, 2, null, 3, null, 4, null),
            false);

        sessionStore.Get(snapshot.Id).Returns(snapshot);
        database.GetSessionCacheAsync(snapshot.Id).Returns((SessionCache?)null);
        database.GetSessionPsstAsync(snapshot.Id).Returns(telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(new SessionTrackPresentationData(null, null, null, null));
        sessionPresentationService.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180), Arg.Any<CancellationToken>())
            .Returns(cacheData);

        var result = await CreateCoordinator().LoadMobileDetailAsync(snapshot.Id, new SessionPresentationDimensions(320, 180));

        var built = Assert.IsType<SessionMobileLoadResult.BuiltCache>(result);
        Assert.Equal("front-travel", built.Data.FrontTravelHistogram);
        await database.Received(1).PutSessionCacheAsync(Arg.Is<SessionCache>(cache =>
            cache.SessionId == snapshot.Id && cache.FrontTravelHistogram == "front-travel"));
    }

    [Fact]
    public async Task LoadMobileDetailAsync_ReturnsTelemetryPending_WhenDownloadUnavailable()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        database.GetSessionCacheAsync(snapshot.Id).Returns((SessionCache?)null);
        database.GetSessionPsstAsync(snapshot.Id).Returns(Task.FromResult<TelemetryData?>(null));
        http.GetSessionPsstAsync(snapshot.Id).Returns((byte[]?)null);

        var result = await CreateCoordinator().LoadMobileDetailAsync(snapshot.Id, new SessionPresentationDimensions(320, 180));

        Assert.IsType<SessionMobileLoadResult.TelemetryPending>(result);
    }

    [Fact]
    public async Task LoadMobileDetailAsync_ReturnsFailed_WhenSnapshotClaimsTelemetryButBlobMissing()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        database.GetSessionCacheAsync(snapshot.Id).Returns((SessionCache?)null);
        database.GetSessionPsstAsync(snapshot.Id).Returns(Task.FromResult<TelemetryData?>(null));

        var result = await CreateCoordinator().LoadMobileDetailAsync(snapshot.Id, new SessionPresentationDimensions(320, 180));

        Assert.IsType<SessionMobileLoadResult.Failed>(result);
    }

    [Fact]
    public async Task LoadMobileDetailAsync_ReturnsFailed_WhenPresentationFails()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.Create();
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        database.GetSessionCacheAsync(snapshot.Id).Returns((SessionCache?)null);
        database.GetSessionPsstAsync(snapshot.Id).Returns(telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(new SessionTrackPresentationData(null, null, null, null));
        sessionPresentationService.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("render failed"));

        var result = await CreateCoordinator().LoadMobileDetailAsync(snapshot.Id, new SessionPresentationDimensions(320, 180));

        Assert.IsType<SessionMobileLoadResult.Failed>(result);
    }

    [Fact]
    public async Task LoadMobileDetailAsync_CancellationDuringCacheBuild_SkipsCacheWrite()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.Create();
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        database.GetSessionCacheAsync(snapshot.Id).Returns((SessionCache?)null);
        database.GetSessionPsstAsync(snapshot.Id).Returns(telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(new SessionTrackPresentationData(null, null, null, null));
        sessionPresentationService.BuildCachePresentation(telemetry, new SessionPresentationDimensions(320, 180), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var token = callInfo.ArgAt<CancellationToken>(2);
                token.ThrowIfCancellationRequested();
                return new SessionCachePresentationData(
                    "front-travel",
                    null,
                    "front-velocity",
                    null,
                    null,
                    null,
                    new SessionDamperPercentages(1, null, 2, null, 3, null, 4, null),
                    false);
            });

        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateCoordinator().LoadMobileDetailAsync(
                snapshot.Id,
                new SessionPresentationDimensions(320, 180),
                cancellationTokenSource.Token));

        await database.DidNotReceive().PutSessionCacheAsync(Arg.Any<SessionCache>());
    }

    // ----- Sync arrival handlers -----

    [AvaloniaFact]
    public async Task Constructor_SubscribesToSyncEvents_AndUpsertsOnSessionDataArrived()
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        var coordinator = CreateCoordinator(sync);

        var sessionId = Guid.NewGuid();
        var fresh = new Session(sessionId, "n", "", null) { Updated = 4, HasProcessedData = true };
        database.GetSessionAsync(sessionId).Returns(fresh);

        sync.SessionDataArrived += Raise.EventWith(sync, new SessionDataArrivedEventArgs(sessionId));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(s =>
            s.Id == sessionId && s.Updated == 4));
    }

    [AvaloniaFact]
    public async Task Constructor_OnSessionDataArrived_IgnoresDatabaseFailure()
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        _ = CreateCoordinator(sync);

        var sessionId = Guid.NewGuid();
        database.GetSessionAsync(sessionId).ThrowsAsync(new InvalidOperationException());

        sync.SessionDataArrived += Raise.EventWith(sync, new SessionDataArrivedEventArgs(sessionId));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        sessionStore.DidNotReceive().Upsert(Arg.Any<SessionSnapshot>());
    }

    [AvaloniaFact]
    public async Task Constructor_OnSynchronizationDataArrived_RemovesDeletedSessionsAndUpsertsLive()
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        var coordinator = CreateCoordinator(sync);

        var liveId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var fresh = new Session(liveId, "live", "", null) { Updated = 6 };
        database.GetSessionAsync(liveId).Returns(fresh);

        var data = new SynchronizationData
        {
            Sessions =
            {
                new Session { Id = liveId, Updated = 6 },
                new Session { Id = deletedId, Updated = 6, Deleted = 6 },
            },
        };

        sync.SynchronizationDataArrived += Raise.EventWith(sync, new SynchronizationDataArrivedEventArgs(data));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        sessionStore.Received(1).Remove(deletedId);
        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(s => s.Id == liveId && s.Updated == 6));
    }

    [AvaloniaFact]
    public async Task Constructor_OnSynchronizationDataArrived_IgnoresDatabaseFailure()
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        _ = CreateCoordinator(sync);

        var liveId = Guid.NewGuid();
        database.GetSessionAsync(liveId).ThrowsAsync(new InvalidOperationException());

        var data = new SynchronizationData
        {
            Sessions =
            {
                new Session { Id = liveId, Updated = 6 },
            },
        };

        sync.SynchronizationDataArrived += Raise.EventWith(sync, new SynchronizationDataArrivedEventArgs(data));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        sessionStore.DidNotReceive().Upsert(Arg.Any<SessionSnapshot>());
        sessionStore.DidNotReceive().Remove(Arg.Any<Guid>());
    }

    [Fact]
    public void Constructor_DoesNotThrow_WhenSyncServerNull()
    {
        // Smoke test: with no sync server we should still get a working
        // coordinator and never NRE on construction.
        var coordinator = CreateCoordinator(sync: null);
        Assert.NotNull(coordinator);
    }
}
