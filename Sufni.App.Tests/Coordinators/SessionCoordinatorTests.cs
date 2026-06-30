using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Coordinators;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.Services;
using Sufni.App.Tests.TestSupport;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Contracts.SessionGraph;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Extensibility.Database;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.SessionGraph;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.Tests.Coordinators;

[Collection("Ui")]
public class SessionCoordinatorTests
{
    private readonly ISessionStoreWriter sessionStore = Substitute.For<ISessionStoreWriter>();
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ISessionTelemetryWriter sessionTelemetryWriter = Substitute.For<ISessionTelemetryWriter>();
    private readonly TestSessionTelemetryProcessor sessionTelemetryProcessor = new();
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository = Substitute.For<IRecordedSessionSourceRepository>();
    private readonly ISynchronizableRepository<Setup> setupRepository = Substitute.For<ISynchronizableRepository<Setup>>();
    private readonly ISynchronizableRepository<Bike> bikeRepository = Substitute.For<ISynchronizableRepository<Bike>>();
    private readonly ISynchronizableRepository<Track> trackEntityRepository = Substitute.For<ISynchronizableRepository<Track>>();
    private readonly ISynchronizableRepository<Session> sessionEntityRepository = Substitute.For<ISynchronizableRepository<Session>>();
    private readonly ISessionCacheStore sessionCacheStore = Substitute.For<ISessionCacheStore>();
    private readonly IHttpApiService http = Substitute.For<IHttpApiService>();
    private readonly ITrackCoordinator trackCoordinator = TestCoordinatorSubstitutes.Track();
    private readonly ISessionPresentationService sessionPresentationService = Substitute.For<ISessionPresentationService>();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>().WithDefaultSelectedLayerChanges();
    private readonly ISessionPreferences sessionPreferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly IRecordedSessionSourceStoreWriter sourceStore = Substitute.For<IRecordedSessionSourceStoreWriter>();
    private readonly IRecordedSessionDomainQuery domainQuery = Substitute.For<IRecordedSessionDomainQuery>();
    private readonly IRecordedSessionGraph recordedSessionGraph = Substitute.For<IRecordedSessionGraph>();
    private readonly IRecordedSessionReprocessor reprocessor = Substitute.For<IRecordedSessionReprocessor>();
    private readonly IRecordedSessionDataReader recordedSessionDataReader = Substitute.For<IRecordedSessionDataReader>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = new InlineBackgroundTaskRunner();
    private readonly IUiThreadDispatcher uiThreadDispatcher = new InlineUiThreadDispatcher();
    private readonly IEditorFactory editorFactory = Substitute.For<IEditorFactory>();
    private readonly IExtensionCascadeService extensionCascade = Substitute.For<IExtensionCascadeService>();
    private readonly ISessionRecomputeEngine recomputeEngine = Substitute.For<ISessionRecomputeEngine>();

    public SessionCoordinatorTests()
    {
        tileLayerService.AvailableLayers.Returns([]);
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);
        sessionPreferences.GetRecordedAsync(Arg.Any<Guid>())
            .Returns(Task.FromResult(SessionPreferences.Default));
        sessionPreferences.RemoveRecordedAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);
        sessionPreferences.UpdateRecordedAsync(Arg.Any<Guid>(), Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .Returns(Task.CompletedTask);
    }

    private SessionLoader CreateLoader() =>
        new(
            sessionStore,
            sessionRepository,
            sessionTelemetryWriter,
            sessionTelemetryProcessor,
            sessionCacheStore,
            http,
            backgroundTaskRunner,
            trackCoordinator,
            sessionPresentationService,
            domainQuery);

    private void SetLocalTelemetry(Guid sessionId, TelemetryData? telemetry)
    {
        if (telemetry is null)
        {
            sessionRepository.GetSessionRawPsstAsync(sessionId).Returns(Task.FromResult<byte[]?>(null));
            return;
        }

        var raw = sessionId.ToByteArray();
        sessionTelemetryProcessor.Map(raw, telemetry);
        sessionRepository.GetSessionRawPsstAsync(sessionId).Returns(raw);
    }

    private SessionCommandService CreateCommandService() =>
        new(
            sessionStore,
            sessionRepository,
            sessionTelemetryWriter,
            setupRepository,
            bikeRepository,
            trackEntityRepository,
            sessionEntityRepository,
            recordedSessionSourceRepository,
            sourceStore,
            reprocessor,
            backgroundTaskRunner,
            sessionPreferences,
            shell,
            recomputeEngine,
            () => editorFactory,
            extensionCascade);

    private SessionCoordinator CreateCoordinator() =>
        new(
            sessionStore,
            CreateLoader(),
            CreateCommandService(),
            () => editorFactory);

    private SessionSyncApplier CreateSyncApplier(ISynchronizationServerService? sync = null) =>
        new(
            sessionStore,
            sessionRepository,
            recordedSessionSourceRepository,
            sourceStore,
            uiThreadDispatcher,
            sync);

    // ----- OpenEditAsync -----

    [Fact]
    public async Task OpenEditAsync_NoOp_WhenSnapshotMissing()
    {
        sessionStore.Get(Arg.Any<Guid>()).Returns((SessionSnapshot?)null);

        await CreateCoordinator().OpenEditAsync(Guid.NewGuid());

        editorFactory.DidNotReceive().OpenSessionDetail(Arg.Any<SessionSnapshot>());
    }

    [Fact]
    public async Task OpenEditAsync_OpensSessionDetail_ThroughFactory()
    {
        var snapshot = TestSnapshots.Session();
        sessionStore.Get(snapshot.Id).Returns(snapshot);

        await CreateCoordinator().OpenEditAsync(snapshot.Id);

        editorFactory.Received(1).OpenSessionDetail(snapshot);
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
        sessionRepository.GetSessionAsync(existing.Id).Returns(fresh);

        var result = await CreateCoordinator().SaveAsync(session, baselineUpdated: 5);

        await sessionRepository.Received(1).PutSessionAsync(session);
        await sessionRepository.Received(1).GetSessionAsync(existing.Id);
        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(s =>
            s.Id == existing.Id && s.Name == "renamed" && s.Updated == 7 && s.HasProcessedData));
        shell.Received(1).GoBack();
        var saved = Assert.IsType<SessionSaveResult.Saved>(result);
        Assert.Equal(7, saved.NewBaselineUpdated);
    }

    [Fact]
    public async Task SaveAsync_ReturnsFailed_WhenRefetchReturnsNull()
    {
        var existing = TestSnapshots.Session(updated: 5);
        sessionStore.Get(existing.Id).Returns(existing);
        sessionRepository.GetSessionAsync(existing.Id).Returns((Session?)null);

        var session = new Session(existing.Id, "renamed", "", null);

        var result = await CreateCoordinator().SaveAsync(session, baselineUpdated: 5);

        Assert.IsType<SessionSaveResult.Failed>(result);
        sessionStore.DidNotReceive().Upsert(Arg.Any<SessionSnapshot>());
        shell.DidNotReceive().GoBack();
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
        await sessionRepository.DidNotReceive().PutSessionAsync(Arg.Any<Session>());
        sessionStore.DidNotReceive().Upsert(Arg.Any<SessionSnapshot>());
        shell.DidNotReceive().GoBack();
    }

    [Fact]
    public async Task SaveAsync_ReturnsFailed_WhenPutSessionThrows()
    {
        var existing = TestSnapshots.Session(updated: 5);
        sessionStore.Get(existing.Id).Returns(existing);
        sessionRepository.PutSessionAsync(Arg.Any<Session>()).ThrowsAsync(new InvalidOperationException("disk full"));

        var session = new Session(existing.Id, "x", "", null);

        var result = await CreateCoordinator().SaveAsync(session, baselineUpdated: 5);

        Assert.IsType<SessionSaveResult.Failed>(result);
        sessionStore.DidNotReceive().Upsert(Arg.Any<SessionSnapshot>());
        shell.DidNotReceive().GoBack();
    }

    [Fact]
    public async Task SaveLiveCaptureAsync_PersistsTrackSessionAndUpsertsFreshSnapshot()
    {
        var capture = CreateLiveCapturePackage(withGps: true);
        var session = new Session(Guid.NewGuid(), "live session", "desc", capture.Context.SetupId, capture.TelemetryCapture.Metadata.Timestamp);
        var fresh = new Session(session.Id, session.Name, session.Description, session.Setup)
        {
            Updated = 9,
            HasProcessedData = true,
        };
        SeedLiveCaptureDependencies(capture);
        sessionTelemetryWriter
            .PutProcessedSessionAsync(
                Arg.Any<Session>(),
                Arg.Any<Track?>(),
                Arg.Any<RecordedSessionSource?>())
            .Returns(callInfo =>
            {
                var savedTrack = callInfo.ArgAt<Track?>(1);
                fresh.FullTrack = savedTrack?.Id;
                return Task.FromResult(fresh);
            });

        var result = await CreateCoordinator().SaveLiveCaptureAsync(session, capture, SessionPreferences.Default);

        await trackEntityRepository.DidNotReceive().PutAsync(Arg.Any<Track>());
        await reprocessor.Received(1).ReprocessAsync(
            Arg.Is<RecordedSessionDomainSnapshot>(domain =>
                domain.Session.Id == session.Id &&
                domain.Setup!.Id == capture.Context.SetupId &&
                domain.Bike!.Id == capture.Context.BikeId &&
                domain.Source!.SourceKind == RecordedSessionSourceKind.LiveCapture),
            Arg.Is<RecordedSessionSource>(source =>
                source.SessionId == session.Id &&
                source.SourceKind == RecordedSessionSourceKind.LiveCapture &&
                RecordedSessionSourceHash.Matches(source)),
            Arg.Any<TelemetryProcessingOptions>(),
            Arg.Any<CancellationToken>());
        await sessionTelemetryWriter.Received(1).PutProcessedSessionAsync(
            Arg.Is<Session>(saved =>
                saved.Id == session.Id
                && saved.ProcessedData != null
                && saved.ProcessedData.Length > 0
                && saved.ProcessingFingerprintJson != null),
            Arg.Is<Track>(track =>
                track.Points.Count == 1 &&
                track.Points[0].FixMode == 3 &&
                track.Points[0].Satellites == 12 &&
                track.Points[0].Epe2d == 0.5f &&
                track.Points[0].Epe3d == 0.8f),
            Arg.Is<RecordedSessionSource>(source =>
                source.SessionId == session.Id &&
                source.SourceKind == RecordedSessionSourceKind.LiveCapture &&
                source.SourceName == "live"));
        sourceStore.Received(1).Upsert(Arg.Is<RecordedSessionSourceSnapshot>(source =>
            source.SessionId == session.Id && source.SourceKind == RecordedSessionSourceKind.LiveCapture));
        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(snapshot =>
            snapshot.Id == session.Id && snapshot.Updated == 9 && snapshot.HasProcessedData));

        var saved = Assert.IsType<LiveSessionSaveResult.Saved>(result);
        Assert.Equal(session.Id, saved.SessionId);
        Assert.Equal(9, saved.Updated);
    }

    [Fact]
    public async Task SaveLiveCaptureAsync_SeedsRecordedPreferences_WhenProvided()
    {
        var capture = CreateLiveCapturePackage(withGps: false);
        var session = new Session(Guid.NewGuid(), "live session", "desc", capture.Context.SetupId, capture.TelemetryCapture.Metadata.Timestamp);
        var fresh = new Session(session.Id, session.Name, session.Description, session.Setup)
        {
            Updated = 9,
            HasProcessedData = true,
        };
        var preferences = new SessionPreferences(
            new SessionPlotPreferences(Travel: true, Velocity: false, Imu: true),
            new SessionStatisticsPreferences(
                TravelHistogramMode.DynamicSag,
                VelocityAverageMode.StrokePeakAveraged,
                BalanceDisplacementMode.Travel,
                BalanceSpeedMode.HighSpeed,
                SessionAnalysisTargetProfile.DH));
        Func<SessionPreferences, SessionPreferences>? update = null;
        SeedLiveCaptureDependencies(capture);
        sessionTelemetryWriter
            .PutProcessedSessionAsync(
                Arg.Any<Session>(),
                Arg.Any<Track?>(),
                Arg.Any<RecordedSessionSource?>())
            .Returns(Task.FromResult(fresh));
        sessionPreferences.UpdateRecordedAsync(
                session.Id,
                Arg.Do<Func<SessionPreferences, SessionPreferences>>(value => update = value))
            .Returns(Task.CompletedTask);

        var result = await CreateCoordinator().SaveLiveCaptureAsync(session, capture, preferences);

        Assert.IsType<LiveSessionSaveResult.Saved>(result);
        await sessionPreferences.Received(1).UpdateRecordedAsync(session.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
        Assert.NotNull(update);
        Assert.Equal(preferences, update!(SessionPreferences.Default));
    }

    [Fact]
    public async Task SaveLiveCaptureAsync_ReturnsFailed_WhenProcessedPersistenceThrows()
    {
        var capture = CreateLiveCapturePackage(withGps: false);
        var session = new Session(Guid.NewGuid(), "live session", "desc", capture.Context.SetupId, capture.TelemetryCapture.Metadata.Timestamp);
        SeedLiveCaptureDependencies(capture);
        sessionTelemetryWriter
            .PutProcessedSessionAsync(
                Arg.Any<Session>(),
                Arg.Any<Track?>(),
                Arg.Any<RecordedSessionSource?>())
            .ThrowsAsync(new InvalidOperationException("disk full"));

        var result = await CreateCoordinator().SaveLiveCaptureAsync(session, capture, SessionPreferences.Default);

        Assert.IsType<LiveSessionSaveResult.Failed>(result);
        sessionStore.DidNotReceive().Upsert(Arg.Any<SessionSnapshot>());
        sourceStore.DidNotReceive().Upsert(Arg.Any<RecordedSessionSourceSnapshot>());
    }

    [Fact]
    public async Task SaveLiveCaptureAsync_PropagatesCancellation_BeforePersistence()
    {
        var capture = CreateLiveCapturePackage(withGps: false);
        var session = new Session(Guid.NewGuid(), "live session", "desc", capture.Context.SetupId, capture.TelemetryCapture.Metadata.Timestamp);
        SeedLiveCaptureDependencies(capture);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateCoordinator().SaveLiveCaptureAsync(session, capture, SessionPreferences.Default, cancellationTokenSource.Token));

        await sessionTelemetryWriter.DidNotReceive().PutProcessedSessionAsync(
            Arg.Any<Session>(),
            Arg.Any<Track?>(),
            Arg.Any<RecordedSessionSource?>());
        sessionStore.DidNotReceive().Upsert(Arg.Any<SessionSnapshot>());
        sourceStore.DidNotReceive().Upsert(Arg.Any<RecordedSessionSourceSnapshot>());
    }

    // ----- DeleteAsync -----

    [Fact]
    public async Task DeleteAsync_DeletesOrphanedTrack_ClosesAndRemoves()
    {
        var id = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        sessionRepository.GetSessionAsync(id).Returns(new Session(id, "name", "desc", null) { FullTrack = trackId });
        sessionEntityRepository.GetAllAsync().Returns(Task.FromResult(new List<Session>
        {
            new(id, "name", "desc", null) { FullTrack = trackId }
        }));

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.Equal(SessionDeleteOutcome.Deleted, result.Outcome);
        await sessionEntityRepository.Received(1).DeleteAsync(id);
        await extensionCascade.Received(1).ApplyForDeletedCoreEntityAsync(ExtensionCoreEntityKind.Session, id);
        await recordedSessionSourceRepository.Received(1).DeleteRecordedSessionSourceAsync(id);
        sourceStore.Received(1).Remove(id);
        await trackEntityRepository.Received(1).DeleteAsync(trackId);
        await extensionCascade.Received(1).ApplyForDeletedCoreEntityAsync(ExtensionCoreEntityKind.Track, trackId);
        await sessionPreferences.Received(1).RemoveRecordedAsync(id);
        editorFactory.Received(1).CloseSessionDetail(id);
        sessionStore.Received(1).Remove(id);
    }

    [Fact]
    public async Task DeleteAsync_DoesNotDeleteTrack_WhenAnotherSessionStillUsesIt()
    {
        var id = Guid.NewGuid();
        var otherSessionId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        sessionRepository.GetSessionAsync(id).Returns(new Session(id, "name", "desc", null) { FullTrack = trackId });
        sessionEntityRepository.GetAllAsync().Returns(Task.FromResult(new List<Session>
        {
            new(id, "name", "desc", null) { FullTrack = trackId },
            new(otherSessionId, "other", "desc", null) { FullTrack = trackId }
        }));

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.Equal(SessionDeleteOutcome.Deleted, result.Outcome);
        await sessionEntityRepository.Received(1).DeleteAsync(id);
        await extensionCascade.Received(1).ApplyForDeletedCoreEntityAsync(ExtensionCoreEntityKind.Session, id);
        await recordedSessionSourceRepository.Received(1).DeleteRecordedSessionSourceAsync(id);
        sourceStore.Received(1).Remove(id);
        await trackEntityRepository.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
        await extensionCascade.DidNotReceive().ApplyForDeletedCoreEntityAsync(ExtensionCoreEntityKind.Track, Arg.Any<Guid>());
        editorFactory.Received(1).CloseSessionDetail(id);
        sessionStore.Received(1).Remove(id);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsDeleted_WhenTrackCleanupFails()
    {
        var id = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        sessionRepository.GetSessionAsync(id).Returns(new Session(id, "name", "desc", null) { FullTrack = trackId });
        sessionEntityRepository.GetAllAsync().Returns(Task.FromResult(new List<Session>
        {
            new(id, "name", "desc", null) { FullTrack = trackId }
        }));
        trackEntityRepository.DeleteAsync(trackId).ThrowsAsync(new InvalidOperationException("track locked"));

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.Equal(SessionDeleteOutcome.Deleted, result.Outcome);
        await sessionEntityRepository.Received(1).DeleteAsync(id);
        await extensionCascade.Received(1).ApplyForDeletedCoreEntityAsync(ExtensionCoreEntityKind.Session, id);
        await recordedSessionSourceRepository.Received(1).DeleteRecordedSessionSourceAsync(id);
        sourceStore.Received(1).Remove(id);
        await trackEntityRepository.Received(1).DeleteAsync(trackId);
        await extensionCascade.DidNotReceive().ApplyForDeletedCoreEntityAsync(ExtensionCoreEntityKind.Track, trackId);
        editorFactory.Received(1).CloseSessionDetail(id);
        sessionStore.Received(1).Remove(id);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFailed_WhenDatabaseDeleteThrows()
    {
        var id = Guid.NewGuid();
        sessionEntityRepository.DeleteAsync(id).ThrowsAsync(new InvalidOperationException("locked"));

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.Equal(SessionDeleteOutcome.Failed, result.Outcome);
        await extensionCascade.DidNotReceive().ApplyForDeletedCoreEntityAsync(Arg.Any<ExtensionCoreEntityKind>(), Arg.Any<Guid>());
        await sessionPreferences.DidNotReceive().RemoveRecordedAsync(id);
        sessionStore.DidNotReceiveWithAnyArgs().Remove(default);
        editorFactory.DidNotReceive().CloseSessionDetail(Arg.Any<Guid>());
    }

    // ----- Desktop / Mobile load workflows -----

    [Fact]
    public async Task LoadDesktopDetailAsync_ReturnsLoaded_WhenTelemetryPresent()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var percentages = new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8);
        var trackData = new SessionTrackPresentationData(
            Guid.NewGuid(),
            [new TrackPoint(1, 1, 1, 0)],
            [new TrackPoint(2, 2, 2, 0)],
            400.0);

        sessionStore.Get(snapshot.Id).Returns(snapshot);
        SetLocalTelemetry(snapshot.Id, telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(trackData);
        sessionPresentationService
            .CalculateDamperPercentages(
                telemetry,
                Arg.Any<TelemetryTimeRange?>(),
                Arg.Any<VelocityAverageMode>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(percentages);

        var result = await CreateCoordinator().LoadDesktopDetailAsync(snapshot.Id);

        var loaded = Assert.IsType<SessionDesktopLoadResult.Loaded>(result);
        Assert.Same(telemetry, loaded.Data.TelemetryData);
        Assert.Same(trackData.TrackPoints, loaded.Data.TrackPoints);
        Assert.Equal(400.0, loaded.Data.MediaColumnWidth);
        Assert.Equal(percentages, loaded.Data.DamperPercentages);
    }

    [Fact]
    public async Task LoadDesktopDetailAsync_UsesBikeDampingSpeedCutoffs()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var cutoffs = DampingSpeedCutoffs.FromValues(110, 220, 330, 440);
        var bike = TestSnapshots.Bike(updated: 17) with
        {
            FrontCompressionDampingCutoffMmPerSecond = cutoffs.Front.CompressionMmPerSecond,
            FrontReboundDampingCutoffMmPerSecond = cutoffs.Front.ReboundMmPerSecond,
            RearCompressionDampingCutoffMmPerSecond = cutoffs.Rear.CompressionMmPerSecond,
            RearReboundDampingCutoffMmPerSecond = cutoffs.Rear.ReboundMmPerSecond,
        };
        var percentages = new SessionDamperPercentages(11, 12, 13, 14, 15, 16, 17, 18);

        sessionStore.Get(snapshot.Id).Returns(snapshot);
        domainQuery.Get(snapshot.Id).Returns(DomainWithBike(snapshot, bike));
        SetLocalTelemetry(snapshot.Id, telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(new SessionTrackPresentationData(null, null, null, null));
        sessionPresentationService
            .CalculateDamperPercentages(
                telemetry,
                Arg.Any<TelemetryTimeRange?>(),
                Arg.Any<VelocityAverageMode>(),
                Arg.Is<DampingSpeedCutoffs?>(value => value == cutoffs))
            .Returns(percentages);

        var result = await CreateCoordinator().LoadDesktopDetailAsync(snapshot.Id);

        var loaded = Assert.IsType<SessionDesktopLoadResult.Loaded>(result);
        Assert.Equal(cutoffs, loaded.Data.DampingSpeedCutoffs);
        Assert.Equal(percentages, loaded.Data.DamperPercentages);
        Assert.Equal(new DampingSpeedCutoffOwner(bike.Id, bike.Updated), loaded.Data.DampingSpeedCutoffOwner);
    }

    [Fact]
    public async Task LoadDesktopDetailAsync_ReturnsTelemetryPending_WhenTelemetryMissing()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        SetLocalTelemetry(snapshot.Id, null);

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
        SetLocalTelemetry(snapshot.Id, null);

        var result = await CreateCoordinator().LoadDesktopDetailAsync(snapshot.Id);

        Assert.IsType<SessionDesktopLoadResult.Failed>(result);
    }

    [Fact]
    public async Task LoadDesktopDetailAsync_ReturnsFailed_WhenTrackCoordinatorThrows()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        SetLocalTelemetry(snapshot.Id, telemetry);
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
        var telemetry = TestTelemetryData.CreateProcessed();
        var trackData = new SessionTrackPresentationData(Guid.NewGuid(), [], [], 400);
        sessionCacheStore.GetSessionCacheAsync(sessionId).Returns(cache);
        SetLocalTelemetry(sessionId, telemetry);
        trackCoordinator.LoadSessionTrackAsync(sessionId, null, telemetry, Arg.Any<CancellationToken>())
            .Returns(trackData);

        var result = await CreateCoordinator().LoadMobileDetailAsync(sessionId, new SessionPresentationDimensions(320, 180));

        var loaded = Assert.IsType<SessionMobileLoadResult.LoadedFromCache>(result);
        Assert.Equal("cached", loaded.Data.FrontTravelHistogram);
        Assert.Same(telemetry, loaded.Telemetry);
        Assert.Same(trackData, loaded.TrackData);
        await sessionRepository.Received(1).GetSessionRawPsstAsync(sessionId);
        await http.DidNotReceive().GetSessionPsstAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task LoadMobileDetailAsync_ReturnsCacheHit_WithNullTelemetry_WhenLocalTelemetryMissing()
    {
        var sessionId = Guid.NewGuid();
        var cache = new SessionCache { SessionId = sessionId, FrontTravelHistogram = "cached" };
        sessionCacheStore.GetSessionCacheAsync(sessionId).Returns(cache);
        SetLocalTelemetry(sessionId, null);

        var result = await CreateCoordinator().LoadMobileDetailAsync(sessionId, new SessionPresentationDimensions(320, 180));

        var loaded = Assert.IsType<SessionMobileLoadResult.LoadedFromCache>(result);
        Assert.Equal("cached", loaded.Data.FrontTravelHistogram);
        Assert.Null(loaded.Telemetry);
        Assert.Null(loaded.TrackData);
        await sessionRepository.Received(1).GetSessionRawPsstAsync(sessionId);
        await http.DidNotReceive().GetSessionPsstAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task LoadMobileDetailAsync_RecomputesCachedDamperPercentages_WhenBikeCutoffsChangedAndTelemetryIsAvailable()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var cachedCutoffs = DampingSpeedCutoffs.FromValues(100, 100, 100, 100);
        var currentCutoffs = DampingSpeedCutoffs.FromValues(250, 350, 450, 550);
        var bike = TestSnapshots.Bike(updated: 21) with
        {
            FrontCompressionDampingCutoffMmPerSecond = currentCutoffs.Front.CompressionMmPerSecond,
            FrontReboundDampingCutoffMmPerSecond = currentCutoffs.Front.ReboundMmPerSecond,
            RearCompressionDampingCutoffMmPerSecond = currentCutoffs.Rear.CompressionMmPerSecond,
            RearReboundDampingCutoffMmPerSecond = currentCutoffs.Rear.ReboundMmPerSecond,
        };
        var stalePercentages = new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8);
        var currentPercentages = new SessionDamperPercentages(11, 12, 13, 14, 15, 16, 17, 18);
        var cache = new SessionCache
        {
            SessionId = snapshot.Id,
            FrontTravelHistogram = "cached",
            DamperPercentages = stalePercentages,
            DampingSpeedCutoffs = cachedCutoffs,
        };

        sessionStore.Get(snapshot.Id).Returns(snapshot);
        domainQuery.Get(snapshot.Id).Returns(DomainWithBike(snapshot, bike));
        sessionCacheStore.GetSessionCacheAsync(snapshot.Id).Returns(cache);
        SetLocalTelemetry(snapshot.Id, telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(new SessionTrackPresentationData(null, null, null, null));
        sessionPresentationService
            .CalculateDamperPercentages(
                telemetry,
                Arg.Any<TelemetryTimeRange?>(),
                Arg.Any<VelocityAverageMode>(),
                Arg.Is<DampingSpeedCutoffs?>(value => value == currentCutoffs))
            .Returns(currentPercentages);

        var result = await CreateCoordinator().LoadMobileDetailAsync(snapshot.Id, new SessionPresentationDimensions(320, 180));

        var loaded = Assert.IsType<SessionMobileLoadResult.LoadedFromCache>(result);
        Assert.Equal(currentPercentages, loaded.Data.DamperPercentages);
        Assert.Equal(currentCutoffs, loaded.Data.DampingSpeedCutoffs);
        Assert.Equal(new DampingSpeedCutoffOwner(bike.Id, bike.Updated), loaded.Data.DampingSpeedCutoffOwner);
        await sessionCacheStore.DidNotReceive().PutSessionCacheAsync(Arg.Any<SessionCache>());
    }

    [Fact]
    public async Task LoadMobileDetailAsync_BuildsAndPersistsCache_WhenCacheMissing()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var trackData = new SessionTrackPresentationData(Guid.NewGuid(), [], [], 400);
        var cacheData = new SessionCachePresentationData(
            "front-travel",
            null,
            "front-velocity",
            null,
            null,
            null,
            new SessionDamperPercentages(1, null, 2, null, 3, null, 4, null),
            DampingSpeedCutoffs.Default,
            false);

        sessionStore.Get(snapshot.Id).Returns(snapshot);
        sessionCacheStore.GetSessionCacheAsync(snapshot.Id).Returns((SessionCache?)null);
        SetLocalTelemetry(snapshot.Id, telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(trackData);
        sessionPresentationService.BuildCachePresentation(
                telemetry,
                new SessionPresentationDimensions(320, 180),
                Arg.Any<CancellationToken>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(cacheData);

        var result = await CreateCoordinator().LoadMobileDetailAsync(snapshot.Id, new SessionPresentationDimensions(320, 180));

        var built = Assert.IsType<SessionMobileLoadResult.BuiltCache>(result);
        Assert.Equal("front-travel", built.Data.FrontTravelHistogram);
        Assert.Same(telemetry, built.Telemetry);
        Assert.Same(trackData, built.TrackData);
        await sessionCacheStore.Received(1).PutSessionCacheAsync(Arg.Is<SessionCache>(cache =>
            cache.SessionId == snapshot.Id && cache.FrontTravelHistogram == "front-travel"));
    }

    [Fact]
    public async Task LoadMobileDetailAsync_BuildsAndPersistsCache_WithCurrentBikeCutoffs()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var cutoffs = DampingSpeedCutoffs.FromValues(125, 235, 345, 455);
        var bike = TestSnapshots.Bike(updated: 31) with
        {
            FrontCompressionDampingCutoffMmPerSecond = cutoffs.Front.CompressionMmPerSecond,
            FrontReboundDampingCutoffMmPerSecond = cutoffs.Front.ReboundMmPerSecond,
            RearCompressionDampingCutoffMmPerSecond = cutoffs.Rear.CompressionMmPerSecond,
            RearReboundDampingCutoffMmPerSecond = cutoffs.Rear.ReboundMmPerSecond,
        };
        var cacheData = new SessionCachePresentationData(
            "front-travel",
            null,
            "front-velocity",
            null,
            null,
            null,
            new SessionDamperPercentages(1, null, 2, null, 3, null, 4, null),
            cutoffs,
            false);

        sessionStore.Get(snapshot.Id).Returns(snapshot);
        domainQuery.Get(snapshot.Id).Returns(DomainWithBike(snapshot, bike));
        sessionCacheStore.GetSessionCacheAsync(snapshot.Id).Returns((SessionCache?)null);
        SetLocalTelemetry(snapshot.Id, telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(new SessionTrackPresentationData(null, null, null, null));
        sessionPresentationService.BuildCachePresentation(
                telemetry,
                new SessionPresentationDimensions(320, 180),
                Arg.Any<CancellationToken>(),
                Arg.Is<DampingSpeedCutoffs?>(value => value == cutoffs))
            .Returns(cacheData);

        var result = await CreateCoordinator().LoadMobileDetailAsync(snapshot.Id, new SessionPresentationDimensions(320, 180));

        var built = Assert.IsType<SessionMobileLoadResult.BuiltCache>(result);
        Assert.Equal(cutoffs, built.Data.DampingSpeedCutoffs);
        Assert.Equal(new DampingSpeedCutoffOwner(bike.Id, bike.Updated), built.Data.DampingSpeedCutoffOwner);
        await sessionCacheStore.Received(1).PutSessionCacheAsync(Arg.Is<SessionCache>(cache =>
            cache.SessionId == snapshot.Id &&
            cache.DampingSpeedCutoffs == cutoffs));
    }

    [Fact]
    public async Task LoadMobileDetailAsync_ReturnsTelemetryPending_WhenDownloadUnavailable()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        sessionCacheStore.GetSessionCacheAsync(snapshot.Id).Returns((SessionCache?)null);
        SetLocalTelemetry(snapshot.Id, null);
        http.GetSessionPsstAsync(snapshot.Id).Returns((SessionDataTransfer?)null);

        var result = await CreateCoordinator().LoadMobileDetailAsync(snapshot.Id, new SessionPresentationDimensions(320, 180));

        Assert.IsType<SessionMobileLoadResult.TelemetryPending>(result);
    }

    [Fact]
    public async Task LoadMobileDetailAsync_ReturnsFailed_WhenSnapshotClaimsTelemetryButBlobMissing()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        sessionCacheStore.GetSessionCacheAsync(snapshot.Id).Returns((SessionCache?)null);
        SetLocalTelemetry(snapshot.Id, null);

        var result = await CreateCoordinator().LoadMobileDetailAsync(snapshot.Id, new SessionPresentationDimensions(320, 180));

        Assert.IsType<SessionMobileLoadResult.Failed>(result);
    }

    [Fact]
    public async Task LoadMobileDetailAsync_ReturnsFailed_WhenPresentationFails()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        sessionCacheStore.GetSessionCacheAsync(snapshot.Id).Returns((SessionCache?)null);
        SetLocalTelemetry(snapshot.Id, telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(new SessionTrackPresentationData(null, null, null, null));
        sessionPresentationService.BuildCachePresentation(
                telemetry,
                new SessionPresentationDimensions(320, 180),
                Arg.Any<CancellationToken>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Throws(new InvalidOperationException("render failed"));

        var result = await CreateCoordinator().LoadMobileDetailAsync(snapshot.Id, new SessionPresentationDimensions(320, 180));

        Assert.IsType<SessionMobileLoadResult.Failed>(result);
    }

    [Fact]
    public async Task LoadMobileDetailAsync_CancellationDuringCacheBuild_SkipsCacheWrite()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        sessionCacheStore.GetSessionCacheAsync(snapshot.Id).Returns((SessionCache?)null);
        SetLocalTelemetry(snapshot.Id, telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(new SessionTrackPresentationData(null, null, null, null));
        sessionPresentationService.BuildCachePresentation(
                telemetry,
                new SessionPresentationDimensions(320, 180),
                Arg.Any<CancellationToken>(),
                Arg.Any<DampingSpeedCutoffs?>())
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
                    DampingSpeedCutoffs.Default,
                    false);
            });

        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateCoordinator().LoadMobileDetailAsync(
                snapshot.Id,
                new SessionPresentationDimensions(320, 180),
                cancellationTokenSource.Token));

        await sessionCacheStore.DidNotReceive().PutSessionCacheAsync(Arg.Any<SessionCache>());
    }

    // ----- Sync arrival handlers -----

    [AvaloniaFact]
    public async Task Constructor_SubscribesToSyncEvents_AndUpsertsOnSessionDataArrived()
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        _ = CreateSyncApplier(sync);

        var sessionId = Guid.NewGuid();
        var fresh = new Session(sessionId, "n", "", null) { Updated = 4, HasProcessedData = true };
        sessionRepository.GetSessionAsync(sessionId).Returns(fresh);

        sync.SessionDataArrived += Raise.EventWith(sync, new SessionDataArrivedEventArgs(sessionId));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(s =>
            s.Id == sessionId && s.Updated == 4));
    }

    [AvaloniaFact]
    public async Task Constructor_OnSessionDataArrived_IgnoresDatabaseFailure()
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        _ = CreateSyncApplier(sync);

        var sessionId = Guid.NewGuid();
        sessionRepository.GetSessionAsync(sessionId).ThrowsAsync(new InvalidOperationException());

        sync.SessionDataArrived += Raise.EventWith(sync, new SessionDataArrivedEventArgs(sessionId));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        sessionStore.DidNotReceive().Upsert(Arg.Any<SessionSnapshot>());
    }

    [AvaloniaFact]
    public async Task Constructor_SubscribesToSourceEvents_AndUpsertsOnSessionSourceDataArrived()
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        _ = CreateSyncApplier(sync);

        var source = CreateRecordedSource(Guid.NewGuid());
        recordedSessionSourceRepository.GetRecordedSessionSourceAsync(source.SessionId).Returns(source);

        sync.SessionSourceDataArrived += Raise.EventWith(sync, new SessionDataArrivedEventArgs(source.SessionId));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        sourceStore.Received(1).Upsert(Arg.Is<RecordedSessionSourceSnapshot>(snapshot =>
            snapshot.SessionId == source.SessionId &&
            snapshot.SourceHash == source.SourceHash));
    }

    [AvaloniaFact]
    public async Task Constructor_OnSessionSourceDataArrived_IgnoresDatabaseFailure()
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        _ = CreateSyncApplier(sync);

        var sessionId = Guid.NewGuid();
        recordedSessionSourceRepository.GetRecordedSessionSourceAsync(sessionId).ThrowsAsync(new InvalidOperationException());

        sync.SessionSourceDataArrived += Raise.EventWith(sync, new SessionDataArrivedEventArgs(sessionId));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        sourceStore.DidNotReceive().Upsert(Arg.Any<RecordedSessionSourceSnapshot>());
    }

    [AvaloniaFact]
    public async Task Constructor_OnSynchronizationDataArrived_RemovesDeletedSessionsAndUpsertsLive()
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        _ = CreateSyncApplier(sync);

        var liveId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var fresh = new Session(liveId, "live", "", null) { Updated = 6 };
        sessionRepository.GetSessionAsync(liveId).Returns(fresh);

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
        _ = CreateSyncApplier(sync);

        var liveId = Guid.NewGuid();
        sessionRepository.GetSessionAsync(liveId).ThrowsAsync(new InvalidOperationException());

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

    private static RecordedSessionSource CreateRecordedSource(Guid sessionId)
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        return new RecordedSessionSource
        {
            SessionId = sessionId,
            SourceKind = RecordedSessionSourceKind.ImportedSst,
            SourceName = "recompute.SST",
            SchemaVersion = 1,
            SourceHash = RecordedSessionSourceHash.Compute(
                RecordedSessionSourceKind.ImportedSst,
                "recompute.SST",
                1,
                payload),
            Payload = payload
        };
    }

    private static RecordedSessionDomainSnapshot DomainWithBike(SessionSnapshot session, BikeSnapshot bike) => new(
        session,
        null,
        bike,
        null,
        null,
        null,
        new SessionStaleness.Current(),
        DerivedChangeKind.None);

    private static LiveSessionCapturePackage CreateLiveCapturePackage(bool withGps)
    {
        var context = new LiveDaqSessionContext(
            IdentityKey: "board-1",
            BoardId: Guid.NewGuid(),
            DisplayName: "Board 1",
            SetupId: Guid.NewGuid(),
            SetupName: "race",
            BikeId: Guid.NewGuid(),
            BikeName: "demo",
            BikeData: new BikeData(180, 170, measurement => measurement / 10.0, measurement => measurement / 10.0),
            TravelCalibration: new LiveDaqTravelCalibration(null, null),
            DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
            DampingSpeedCutoffOwner: new DampingSpeedCutoffOwner(Guid.Empty, 0));

        return new LiveSessionCapturePackage(
            context,
            new LiveTelemetryCapture(
                Metadata: new Metadata
                {
                    SourceName = "live",
                    Version = 4,
                    SampleRate = 200,
                    Timestamp = 1_704_164_646,
                    Duration = 0.03,
                },
                BikeData: context.BikeData,
                FrontMeasurements: [1000, 1010, 1020, 1030, 1040, 1050],
                RearMeasurements: [1100, 1110, 1120, 1130, 1140, 1150],
                ImuData: null,
                GpsData: withGps
                    ?
                    [
                        new GpsRecord(
                            Timestamp: new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc),
                            Latitude: 42.6977,
                            Longitude: 23.3219,
                            Altitude: 600,
                            Speed: 10,
                            Heading: 90,
                            FixMode: 3,
                            Satellites: 12,
                            Epe2d: 0.5f,
                            Epe3d: 0.8f),
                    ]
                    : null,
                Markers: []));
    }

    private void SeedLiveCaptureDependencies(LiveSessionCapturePackage capture)
    {
        var setup = new Setup(capture.Context.SetupId, capture.Context.SetupName)
        {
            BikeId = capture.Context.BikeId
        };
        var bike = new Bike(capture.Context.BikeId, capture.Context.BikeName)
        {
            HeadAngle = 63,
            ForkStroke = capture.Context.BikeData.FrontMaxTravel,
            ShockStroke = capture.Context.BikeData.RearMaxTravel
        };

        setupRepository.GetAsync(capture.Context.SetupId).Returns(Task.FromResult<Setup?>(setup));
        bikeRepository.GetAsync(capture.Context.BikeId).Returns(Task.FromResult<Bike?>(bike));
        reprocessor
            .ReprocessAsync(
                Arg.Any<RecordedSessionDomainSnapshot>(),
                Arg.Any<RecordedSessionSource>(),
                Arg.Any<TelemetryProcessingOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var domain = callInfo.ArgAt<RecordedSessionDomainSnapshot>(0);
                var source = callInfo.ArgAt<RecordedSessionSource>(1);
                var processingOptions = callInfo.ArgAt<TelemetryProcessingOptions>(2);
                var telemetryData = TelemetryData.FromLiveCapture(capture.TelemetryCapture, processingOptions);
                var fullTrack = telemetryData.GpsData is { Length: > 0 }
                    ? Track.FromGpsRecords(telemetryData.GpsData)
                    : null;
                var fingerprint = new ProcessingFingerprint(
                    SchemaVersion: 2,
                    ProcessingVersion: 1,
                    SetupId: domain.Setup!.Id,
                    BikeId: domain.Bike!.Id,
                    TrackProjectionVersion: 1,
                    DependencyHash: "dependency",
                    SourceHash: source.SourceHash);
                return Task.FromResult(new RecordedSessionReprocessResult(telemetryData, fullTrack, fingerprint));
            });
    }
}
