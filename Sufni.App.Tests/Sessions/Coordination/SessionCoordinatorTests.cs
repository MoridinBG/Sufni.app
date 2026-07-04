using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

using Sufni.App.Bikes.Models;
using Sufni.App.Extensibility.RecordedSessions;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Extensions;

namespace Sufni.App.Tests.Sessions.Coordination;

[Collection("Ui")]
public class SessionCoordinatorTests
{
    private readonly ISessionStoreWriter sessionStore = Substitute.For<ISessionStoreWriter>();
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ISessionTelemetryWriter sessionTelemetryWriter = Substitute.For<ISessionTelemetryWriter>();
    private readonly TestSessionProcessedTelemetryReader processedTelemetryReader = new();
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository = Substitute.For<IRecordedSessionSourceRepository>();
    private readonly ISynchronizableRepository<Setup> setupRepository = Substitute.For<ISynchronizableRepository<Setup>>();
    private readonly ISynchronizableRepository<Bike> bikeRepository = Substitute.For<ISynchronizableRepository<Bike>>();
    private readonly ISynchronizableRepository<Track> trackEntityRepository = Substitute.For<ISynchronizableRepository<Track>>();
    private readonly ISynchronizableRepository<Session> sessionEntityRepository = Substitute.For<ISynchronizableRepository<Session>>();
    private readonly IHttpApiService http = Substitute.For<IHttpApiService>();
    private readonly ITrackCoordinator trackCoordinator = TestCoordinatorSubstitutes.Track();
    private readonly ISessionPresentationService sessionPresentationService = Substitute.For<ISessionPresentationService>();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>().WithDefaultSelectedLayerChanges();
    private readonly ISessionPreferences sessionPreferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly IRecordedSessionSourceStoreWriter sourceStore = Substitute.For<IRecordedSessionSourceStoreWriter>();
    private readonly IRecordedSessionDomainQuery domainQuery = Substitute.For<IRecordedSessionDomainQuery>();
    private readonly IRecordedSessionProjection recordedSessionProjection = Substitute.For<IRecordedSessionProjection>();
    private readonly IRecordedSessionReprocessor reprocessor = Substitute.For<IRecordedSessionReprocessor>();
    private readonly IRecordedSessionDataReader recordedSessionDataReader = Substitute.For<IRecordedSessionDataReader>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = new InlineBackgroundTaskRunner();
    private readonly IUiThreadDispatcher uiThreadDispatcher = new InlineUiThreadDispatcher();
    private readonly IEditorFactory editorFactory = Substitute.For<IEditorFactory>();
    private readonly ISessionRecomputeEngine recomputeEngine = Substitute.For<ISessionRecomputeEngine>();
    private readonly IRecordedSessionDerivationWindowCache derivationWindowCache = Substitute.For<IRecordedSessionDerivationWindowCache>();
    private readonly IRecordedSessionDerivationWindowProvider derivationWindowProvider = Substitute.For<IRecordedSessionDerivationWindowProvider>();

    public SessionCoordinatorTests()
    {
        tileLayerService.AvailableLayers.Returns([]);
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);
        sessionPreferences.GetRecordedAsync(Arg.Any<Guid>())
            .Returns(Task.FromResult(SessionPreferences.Default));
        sessionPreferences.RemoveRecordedAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);
        sessionPreferences.UpdateRecordedAsync(Arg.Any<Guid>(), Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .Returns(Task.CompletedTask);
        editorFactory.CloseSessionDetail(Arg.Any<Guid>()).Returns(Task.CompletedTask);
        derivationWindowProvider.IsRecordingSourceReferencedAsync(Arg.Any<Guid>())
            .Returns(Task.FromResult(false));
    }

    private SessionLoader CreateLoader() =>
        new(
            sessionStore,
            processedTelemetryReader,
            backgroundTaskRunner,
            trackCoordinator,
            sessionPresentationService,
            domainQuery);

    private void SetLocalTelemetry(Guid sessionId, TelemetryData? telemetry)
    {
        if (telemetry is null)
        {
            processedTelemetryReader.Set(sessionId, null);
            return;
        }

        processedTelemetryReader.Set(sessionId, telemetry);
    }

    private static SessionCachePresentationData CachePresentation(
        SessionDampingPercentages? percentages = null,
        DampingSpeedCutoffs? cutoffs = null) =>
        new(
            FrontTravelDistribution: "front-travel",
            RearTravelDistribution: null,
            FrontVelocityDistribution: "front-velocity",
            RearVelocityDistribution: null,
            CompressionBalance: null,
            ReboundBalance: null,
            DampingPercentages: percentages ?? SessionDampingPercentages.Empty,
            DampingSpeedCutoffs: cutoffs ?? DampingSpeedCutoffs.Default,
            BalanceAvailable: false);

    private SessionCommandService CreateCommandService(UiLayoutProfile layoutProfile = UiLayoutProfile.Workspace) =>
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
            CreateEnvironment(layoutProfile),
            recomputeEngine,
            () => editorFactory,
            derivationWindowCache,
            derivationWindowProvider);

    private SessionCoordinator CreateCoordinator(UiLayoutProfile layoutProfile = UiLayoutProfile.Workspace) =>
        new(
            sessionStore,
            CreateLoader(),
            CreateCommandService(layoutProfile),
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
        shell.DidNotReceive().GoBack();
        var saved = Assert.IsType<SessionSaveResult.Saved>(result);
        Assert.Equal(7, saved.NewBaselineUpdated);
    }

    [Fact]
    public async Task SaveAsync_OnCompact_NavigatesBackAfterSave()
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

        await CreateCoordinator(UiLayoutProfile.Compact).SaveAsync(session, baselineUpdated: 5);

        shell.Received(1).GoBack();
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
                Arg.Any<ProcessedTelemetryPayload>(),
                Arg.Any<Track?>(),
                Arg.Any<RecordedSessionSource?>())
            .Returns(callInfo =>
            {
                var savedTrack = callInfo.ArgAt<Track?>(2);
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
                saved.Id == session.Id),
            Arg.Is<ProcessedTelemetryPayload>(payload =>
                payload.Data.Length > 0
                && payload.FingerprintJson != null),
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
            new SignalDisplayPreferences(Travel: true, Velocity: false, Imu: true),
            new AnalysisPreferences(
                TravelDistributionMode.DynamicSag,
                VelocityAverageMode.StrokePeakAveraged,
                BalanceDisplacementMode.Travel,
                BalanceSpeedMode.HighSpeed,
                SessionInsightsTargetProfile.DH));
        Func<SessionPreferences, SessionPreferences>? update = null;
        SeedLiveCaptureDependencies(capture);
        sessionTelemetryWriter
            .PutProcessedSessionAsync(
                Arg.Any<Session>(),
                Arg.Any<ProcessedTelemetryPayload>(),
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
                Arg.Any<ProcessedTelemetryPayload>(),
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
            Arg.Any<ProcessedTelemetryPayload>(),
            Arg.Any<Track?>(),
            Arg.Any<RecordedSessionSource?>());
        sessionStore.DidNotReceive().Upsert(Arg.Any<SessionSnapshot>());
        sourceStore.DidNotReceive().Upsert(Arg.Any<RecordedSessionSourceSnapshot>());
    }

    // ----- Editing operations -----

    [Fact]
    public async Task CreateDerivedSessionAsync_CreatesMetadataOnlySession_WithSourceAbsoluteOrigin()
    {
        var sourceSessionId = Guid.NewGuid();
        var from = TestSnapshots.Session(
            id: Guid.NewGuid(),
            name: "source",
            description: "desc",
            setupId: Guid.NewGuid(),
            timestamp: 100) with
        {
            GpsOffsetSeconds = 0.25,
            FrontSpringRate = "80 psi",
            RearSpringRate = "450 lb"
        };
        var window = new RecordedSessionDerivationWindow(sourceSessionId, 1.5, 10);
        sessionStore.Get(from.Id).Returns(from);
        derivationWindowCache.Get(from.Id).Returns(window);
        Session? saved = null;
        sessionRepository.PutSessionAsync(Arg.Any<Session>())
            .Returns(call =>
            {
                saved = call.Arg<Session>();
                return Task.FromResult(saved.Id);
            });
        sessionRepository.GetSessionAsync(Arg.Any<Guid>())
            .Returns(_ => Task.FromResult(saved));

        var createdId = await CreateCoordinator().CreateDerivedSessionAsync(from.Id, "source (2)", 3.75);

        Assert.NotNull(saved);
        Assert.Equal(saved!.Id, createdId);
        Assert.Equal("source (2)", saved.Name);
        Assert.Equal(from.Description, saved.Description);
        Assert.Equal(from.SetupId, saved.Setup);
        Assert.Equal(102, saved.Timestamp);
        Assert.Equal(0.5, saved.GpsOffsetSeconds, precision: 6);
        Assert.Equal(from.FrontSpringRate, saved.FrontSpringRate);
        Assert.Equal(from.RearSpringRate, saved.RearSpringRate);
        Assert.Null(saved.ProcessedData);
        Assert.Null(saved.FullTrack);
        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(snapshot => snapshot.Id == saved.Id));
    }

    [Fact]
    public async Task UpdateSessionOriginAsync_ReanchorsTimestampAndGpsOffset_FromCurrentWindow()
    {
        var sessionId = Guid.NewGuid();
        var snapshot = TestSnapshots.Session(
            id: sessionId,
            setupId: Guid.NewGuid(),
            timestamp: 100) with
        {
            GpsOffsetSeconds = 0.25
        };
        derivationWindowCache.Get(sessionId).Returns(new RecordedSessionDerivationWindow(Guid.NewGuid(), 1.5, 10));
        sessionStore.Get(sessionId).Returns(snapshot);
        Session? saved = null;
        sessionRepository.PutSessionAsync(Arg.Any<Session>())
            .Returns(call =>
            {
                saved = call.Arg<Session>();
                return Task.FromResult(saved.Id);
            });
        sessionRepository.GetSessionAsync(sessionId)
            .Returns(_ => Task.FromResult(saved));

        var result = await CreateCoordinator().UpdateSessionOriginAsync(sessionId, 3.75);

        Assert.True(result);
        Assert.NotNull(saved);
        Assert.Equal(102, saved!.Timestamp);
        Assert.Equal(0.5, saved.GpsOffsetSeconds, precision: 6);
        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(value => value.Id == sessionId));
    }

    [Fact]
    public async Task RenameSessionAsync_UpdatesNameWithoutNormalSaveNavigation()
    {
        var sessionId = Guid.NewGuid();
        var snapshot = TestSnapshots.Session(id: sessionId, name: "before", setupId: Guid.NewGuid());
        sessionStore.Get(sessionId).Returns(snapshot);
        Session? saved = null;
        sessionRepository.PutSessionAsync(Arg.Any<Session>())
            .Returns(call =>
            {
                saved = call.Arg<Session>();
                return Task.FromResult(saved.Id);
            });
        sessionRepository.GetSessionAsync(sessionId)
            .Returns(_ => Task.FromResult(saved));

        var result = await CreateCoordinator().RenameSessionAsync(sessionId, "after");

        Assert.True(result);
        Assert.NotNull(saved);
        Assert.Equal("after", saved!.Name);
        shell.DidNotReceive().GoBack();
        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(value => value.Name == "after"));
    }

    // ----- DeleteAsync -----

    [Fact]
    public async Task DeleteAsync_DeletesOrphanedTrack_ClosesAndRemoves()
    {
        var id = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        sessionRepository.GetSessionAsync(id).Returns(new Session(id, "name", "desc", null) { FullTrack = trackId });
        sessionRepository.HasOtherActiveSessionWithFullTrackAsync(trackId, id).Returns(false);

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.Equal(SessionDeleteOutcome.Deleted, result.Outcome);
        await sessionEntityRepository.Received(1).DeleteAsync(id);
        await recordedSessionSourceRepository.Received(1).DeleteRecordedSessionSourceAsync(id);
        sourceStore.Received(1).Remove(id);
        await trackEntityRepository.Received(1).DeleteAsync(trackId);
        await sessionPreferences.Received(1).RemoveRecordedAsync(id);
        await editorFactory.Received(1).CloseSessionDetail(id);
        sessionStore.Received(1).Remove(id);
    }

    [Fact]
    public async Task DeleteAsync_DoesNotDeleteTrack_WhenAnotherSessionStillUsesIt()
    {
        var id = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        sessionRepository.GetSessionAsync(id).Returns(new Session(id, "name", "desc", null) { FullTrack = trackId });
        sessionRepository.HasOtherActiveSessionWithFullTrackAsync(trackId, id).Returns(true);

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.Equal(SessionDeleteOutcome.Deleted, result.Outcome);
        await sessionEntityRepository.Received(1).DeleteAsync(id);
        await recordedSessionSourceRepository.Received(1).DeleteRecordedSessionSourceAsync(id);
        sourceStore.Received(1).Remove(id);
        await trackEntityRepository.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
        await editorFactory.Received(1).CloseSessionDetail(id);
        sessionStore.Received(1).Remove(id);
    }

    [Fact]
    public async Task DeleteAsync_KeepsRecordedSource_WhenAnotherSessionReferencesIt()
    {
        var id = Guid.NewGuid();
        sessionRepository.GetSessionAsync(id).Returns(new Session(id, "name", "desc", null));
        sessionEntityRepository.GetAllAsync().Returns(Task.FromResult(new List<Session>
        {
            new(id, "name", "desc", null)
        }));
        derivationWindowProvider.IsRecordingSourceReferencedAsync(id).Returns(Task.FromResult(true));

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.Equal(SessionDeleteOutcome.Deleted, result.Outcome);
        await sessionEntityRepository.Received(1).DeleteAsync(id);
        await recordedSessionSourceRepository.DidNotReceive().DeleteRecordedSessionSourceAsync(id);
        sourceStore.DidNotReceive().Remove(id);
        sessionStore.Received(1).Remove(id);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsDeleted_WhenTrackCleanupFails()
    {
        var id = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        sessionRepository.GetSessionAsync(id).Returns(new Session(id, "name", "desc", null) { FullTrack = trackId });
        sessionRepository.HasOtherActiveSessionWithFullTrackAsync(trackId, id).Returns(false);
        trackEntityRepository.DeleteAsync(trackId).ThrowsAsync(new InvalidOperationException("track locked"));

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.Equal(SessionDeleteOutcome.Deleted, result.Outcome);
        await sessionEntityRepository.Received(1).DeleteAsync(id);
        await recordedSessionSourceRepository.Received(1).DeleteRecordedSessionSourceAsync(id);
        sourceStore.Received(1).Remove(id);
        await trackEntityRepository.Received(1).DeleteAsync(trackId);
        await editorFactory.Received(1).CloseSessionDetail(id);
        sessionStore.Received(1).Remove(id);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFailed_WhenDatabaseDeleteThrows()
    {
        var id = Guid.NewGuid();
        sessionEntityRepository.DeleteAsync(id).ThrowsAsync(new InvalidOperationException("locked"));

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.Equal(SessionDeleteOutcome.Failed, result.Outcome);
        await sessionPreferences.DidNotReceive().RemoveRecordedAsync(id);
        sessionStore.DidNotReceiveWithAnyArgs().Remove(default);
        await editorFactory.DidNotReceive().CloseSessionDetail(Arg.Any<Guid>());
    }

    // ----- Session detail load workflow -----

    [Fact]
    public async Task LoadDetailAsync_ReturnsLoaded_WhenTelemetryPresent()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var percentages = new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8);
        var dimensions = new SessionPresentationDimensions(320, 180);
        var cacheData = CachePresentation(percentages);
        var trackData = new SessionTrackPresentationData(
            Guid.NewGuid(),
            [new TrackPoint(1, 1, 1, 0)],
            [new TrackPoint(2, 2, 2, 0)],
            400.0);

        sessionStore.Get(snapshot.Id).Returns(snapshot);
        SetLocalTelemetry(snapshot.Id, telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(trackData);
        sessionPresentationService.BuildCachePresentation(
                telemetry,
                dimensions,
                Arg.Any<CancellationToken>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(cacheData);

        var result = await CreateCoordinator().LoadDetailAsync(snapshot.Id, dimensions);

        var loaded = Assert.IsType<SessionDetailLoadResult.Loaded>(result);
        Assert.Same(telemetry, loaded.Data.TelemetryPresentation.TelemetryData);
        Assert.Same(trackData.TrackPoints, loaded.Data.TelemetryPresentation.TrackPoints);
        Assert.Equal(400.0, loaded.Data.TelemetryPresentation.MediaColumnWidth);
        Assert.Equal(percentages, loaded.Data.TelemetryPresentation.DampingPercentages);
        Assert.Equal(cacheData, loaded.Data.CachePresentation with { DampingSpeedCutoffOwner = null });
    }

    [Fact]
    public async Task LoadDetailAsync_UsesBikeDampingSpeedCutoffs()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var dimensions = new SessionPresentationDimensions(320, 180);
        var cutoffs = DampingSpeedCutoffs.FromValues(110, 220, 330, 440);
        var bike = TestSnapshots.Bike(updated: 17) with
        {
            FrontCompressionDampingCutoffMmPerSecond = cutoffs.Front.CompressionMmPerSecond,
            FrontReboundDampingCutoffMmPerSecond = cutoffs.Front.ReboundMmPerSecond,
            RearCompressionDampingCutoffMmPerSecond = cutoffs.Rear.CompressionMmPerSecond,
            RearReboundDampingCutoffMmPerSecond = cutoffs.Rear.ReboundMmPerSecond,
        };
        var percentages = new SessionDampingPercentages(11, 12, 13, 14, 15, 16, 17, 18);
        var cacheData = CachePresentation(percentages, cutoffs);

        sessionStore.Get(snapshot.Id).Returns(snapshot);
        domainQuery.Get(snapshot.Id).Returns(DomainWithBike(snapshot, bike));
        SetLocalTelemetry(snapshot.Id, telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(new SessionTrackPresentationData(null, null, null, null));
        sessionPresentationService.BuildCachePresentation(
                telemetry,
                dimensions,
                Arg.Any<CancellationToken>(),
                Arg.Is<DampingSpeedCutoffs?>(value => value == cutoffs))
            .Returns(cacheData);

        var result = await CreateCoordinator().LoadDetailAsync(snapshot.Id, dimensions);

        var loaded = Assert.IsType<SessionDetailLoadResult.Loaded>(result);
        Assert.Equal(cutoffs, loaded.Data.TelemetryPresentation.DampingSpeedCutoffs);
        Assert.Equal(percentages, loaded.Data.TelemetryPresentation.DampingPercentages);
        Assert.Equal(new DampingSpeedCutoffOwner(bike.Id, bike.Updated), loaded.Data.TelemetryPresentation.DampingSpeedCutoffOwner);
        Assert.Equal(new DampingSpeedCutoffOwner(bike.Id, bike.Updated), loaded.Data.CachePresentation.DampingSpeedCutoffOwner);
    }

    [Fact]
    public async Task LoadDetailAsync_ReturnsIncompleteLocalData_WhenTelemetryMissing()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        SetLocalTelemetry(snapshot.Id, null);

        var result = await CreateCoordinator().LoadDetailAsync(snapshot.Id, new SessionPresentationDimensions(320, 180));

        var incomplete = Assert.IsType<SessionDetailLoadResult.IncompleteLocalData>(result);
        Assert.Equal(snapshot.Id, incomplete.SessionId);
        Assert.True(incomplete.Missing.ProcessedTelemetryBlob);
        Assert.False(incomplete.Missing.RecordedSourceMissingOrHashMismatch);
        await trackCoordinator.DidNotReceive().LoadSessionTrackAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<TelemetryData>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadDetailAsync_DoesNotDownloadMissingTelemetry()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        SetLocalTelemetry(snapshot.Id, null);

        var result = await CreateCoordinator().LoadDetailAsync(snapshot.Id, new SessionPresentationDimensions(320, 180));

        Assert.IsType<SessionDetailLoadResult.IncompleteLocalData>(result);
        await http.DidNotReceive().GetSessionPsstAsync(Arg.Any<Guid>());
        await sessionTelemetryWriter.DidNotReceive().SwapSessionPsstAsync(
            Arg.Any<Guid>(),
            Arg.Any<byte[]>(),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task LoadDetailAsync_ReturnsIncompleteLocalData_WhenSnapshotClaimsTelemetryButBlobMissing()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        SetLocalTelemetry(snapshot.Id, null);

        var result = await CreateCoordinator().LoadDetailAsync(snapshot.Id, new SessionPresentationDimensions(320, 180));

        var incomplete = Assert.IsType<SessionDetailLoadResult.IncompleteLocalData>(result);
        Assert.True(incomplete.Missing.ProcessedTelemetryBlob);
    }

    [Fact]
    public async Task LoadDetailAsync_ReturnsIncompleteLocalData_WhenRecordedSourceMissing()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        domainQuery.Get(snapshot.Id).Returns(DomainWithMissingSource(snapshot));
        SetLocalTelemetry(snapshot.Id, telemetry);

        var result = await CreateCoordinator().LoadDetailAsync(snapshot.Id, new SessionPresentationDimensions(320, 180));

        var incomplete = Assert.IsType<SessionDetailLoadResult.IncompleteLocalData>(result);
        Assert.False(incomplete.Missing.ProcessedTelemetryBlob);
        Assert.True(incomplete.Missing.RecordedSourceMissingOrHashMismatch);
        await trackCoordinator.DidNotReceive().LoadSessionTrackAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<TelemetryData>(),
            Arg.Any<CancellationToken>());
        sessionPresentationService.DidNotReceive().BuildCachePresentation(
            Arg.Any<TelemetryData>(),
            Arg.Any<SessionPresentationDimensions>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<DampingSpeedCutoffs?>());
    }

    [Fact]
    public async Task LoadDetailAsync_ReturnsIncompleteLocalData_WhenRecordedSourceHashDoesNotMatchFingerprint()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        domainQuery.Get(snapshot.Id).Returns(DomainWithSourceHashMismatch(snapshot));
        SetLocalTelemetry(snapshot.Id, telemetry);

        var result = await CreateCoordinator().LoadDetailAsync(snapshot.Id, new SessionPresentationDimensions(320, 180));

        var incomplete = Assert.IsType<SessionDetailLoadResult.IncompleteLocalData>(result);
        Assert.False(incomplete.Missing.ProcessedTelemetryBlob);
        Assert.True(incomplete.Missing.RecordedSourceMissingOrHashMismatch);
        await trackCoordinator.DidNotReceive().LoadSessionTrackAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<TelemetryData>(),
            Arg.Any<CancellationToken>());
        sessionPresentationService.DidNotReceive().BuildCachePresentation(
            Arg.Any<TelemetryData>(),
            Arg.Any<SessionPresentationDimensions>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<DampingSpeedCutoffs?>());
    }

    [Fact]
    public async Task LoadDetailAsync_ReturnsFailed_WhenTrackCoordinatorThrows()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        SetLocalTelemetry(snapshot.Id, telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("track failed"));

        var result = await CreateCoordinator().LoadDetailAsync(snapshot.Id, new SessionPresentationDimensions(320, 180));

        Assert.IsType<SessionDetailLoadResult.Failed>(result);
    }

    [Fact]
    public async Task LoadDetailAsync_BuildsPresentationInMemoryWithoutSessionCachePersistence()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var dimensions = new SessionPresentationDimensions(320, 180);
        var cacheData = CachePresentation(new SessionDampingPercentages(1, null, 2, null, 3, null, 4, null));

        sessionStore.Get(snapshot.Id).Returns(snapshot);
        SetLocalTelemetry(snapshot.Id, telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(new SessionTrackPresentationData(null, null, null, null));
        sessionPresentationService.BuildCachePresentation(
                telemetry,
                dimensions,
                Arg.Any<CancellationToken>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(cacheData);

        var result = await CreateCoordinator().LoadDetailAsync(snapshot.Id, dimensions);

        var loaded = Assert.IsType<SessionDetailLoadResult.Loaded>(result);
        Assert.Equal("front-travel", loaded.Data.CachePresentation.FrontTravelDistribution);
    }

    [Fact]
    public async Task LoadDetailAsync_ReturnsFailed_WhenPresentationFails()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var dimensions = new SessionPresentationDimensions(320, 180);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        SetLocalTelemetry(snapshot.Id, telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(new SessionTrackPresentationData(null, null, null, null));
        sessionPresentationService.BuildCachePresentation(
                telemetry,
                dimensions,
                Arg.Any<CancellationToken>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Throws(new InvalidOperationException("render failed"));

        var result = await CreateCoordinator().LoadDetailAsync(snapshot.Id, dimensions);

        Assert.IsType<SessionDetailLoadResult.Failed>(result);
    }

    [Fact]
    public async Task LoadDetailAsync_CancellationDuringPresentationBuild_DoesNotPersistPresentation()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var dimensions = new SessionPresentationDimensions(320, 180);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        SetLocalTelemetry(snapshot.Id, telemetry);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(new SessionTrackPresentationData(null, null, null, null));
        sessionPresentationService.BuildCachePresentation(
                telemetry,
                dimensions,
                Arg.Any<CancellationToken>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(callInfo =>
            {
                var token = callInfo.ArgAt<CancellationToken>(2);
                token.ThrowIfCancellationRequested();
                return CachePresentation();
            });

        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateCoordinator().LoadDetailAsync(
                snapshot.Id,
                dimensions,
                cancellationTokenSource.Token));

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
        var snapshot = RecordedSessionSourceSnapshot.From(source);
        recordedSessionSourceRepository.GetRecordedSessionSourceSnapshotAsync(source.SessionId).Returns(snapshot);

        sync.SessionSourceDataArrived += Raise.EventWith(sync, new SessionDataArrivedEventArgs(source.SessionId));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        sourceStore.Received(1).Upsert(Arg.Is<RecordedSessionSourceSnapshot>(value =>
            value.SessionId == source.SessionId &&
            value.SourceHash == source.SourceHash));
        await recordedSessionSourceRepository.DidNotReceive().GetRecordedSessionSourceAsync(source.SessionId);
    }

    [AvaloniaFact]
    public async Task Constructor_OnSessionSourceDataArrived_IgnoresDatabaseFailure()
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        _ = CreateSyncApplier(sync);

        var sessionId = Guid.NewGuid();
        recordedSessionSourceRepository.GetRecordedSessionSourceSnapshotAsync(sessionId).ThrowsAsync(new InvalidOperationException());

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
        byte[] payload = [1, 2, 3, 4];
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

    private static RecordedSessionDomainSnapshot DomainWithBike(SessionSnapshot session, BikeSnapshot bike) =>
        new(
            session,
            null,
            bike,
            null,
            null,
            new RecordedSessionSourceSnapshot(
                session.Id,
                RecordedSessionSourceKind.ImportedSst,
                "source.SST",
                1,
                "source-hash"),
            null,
            new SessionStaleness.Current(),
            DerivedChangeKind.None);

    private static RecordedSessionDomainSnapshot DomainWithMissingSource(SessionSnapshot session) => new(
        session,
        null,
        null,
        null,
        null,
        null,
        null,
        new SessionStaleness.MissingRawSource(),
        DerivedChangeKind.None);

    private static RecordedSessionDomainSnapshot DomainWithSourceHashMismatch(SessionSnapshot session)
    {
        const string expectedSourceHash = "expected-source-hash";
        var source = new RecordedSessionSourceSnapshot(
            session.Id,
            RecordedSessionSourceKind.ImportedSst,
            "source.SST",
            1,
            "actual-source-hash");
        var persisted = new ProcessingFingerprint(
            SchemaVersion: 3,
            ProcessingVersion: TelemetryProcessingVersion.Current,
            SetupId: Guid.NewGuid(),
            BikeId: Guid.NewGuid(),
            TrackProjectionVersion: 1,
            DependencyHash: "dependency-hash",
            SourceHash: expectedSourceHash);

        return new RecordedSessionDomainSnapshot(
            session,
            null,
            null,
            null,
            persisted,
            source,
            null,
            new SessionStaleness.DependencyHashChanged(),
            DerivedChangeKind.None);
    }

    private static IAppEnvironment CreateEnvironment(UiLayoutProfile layoutProfile) =>
        new AppEnvironment(
            DefaultLayoutProfile: layoutProfile,
            LayoutProfile: layoutProfile,
            Capabilities: new AppCapabilities(
                CanHostSyncServer: true,
                CanPairAsClient: true,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true),
            Input: new InputCapabilities(
                HasPointer: true,
                HasTouch: true,
                HasKeyboard: true,
                SupportsLongPressContextMenu: true));

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
                return Task.FromResult(new RecordedSessionReprocessResult(
                    new ProcessedTelemetryPayload(
                        telemetryData,
                        telemetryData.BinaryForm,
                        AppJson.Serialize(fingerprint)),
                    fullTrack,
                    fingerprint));
            });
    }
}
