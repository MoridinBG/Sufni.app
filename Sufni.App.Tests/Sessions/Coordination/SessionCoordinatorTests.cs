using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Extensibility.RecordedSessions;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Shared.Stores;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Extensions;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Coordination;

[Collection("Ui")]
public class SessionCoordinatorTests
{
    private readonly ISessionStoreWriter sessionStore = Substitute.For<ISessionStoreWriter>();
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ISessionTelemetryWriter sessionTelemetryWriter = Substitute.For<ISessionTelemetryWriter>();
    private readonly TestSessionProcessedTelemetryReader processedTelemetryReader = new();
    private readonly ISynchronizableRepository<Setup> setupRepository = Substitute.For<ISynchronizableRepository<Setup>>();
    private readonly ISynchronizableRepository<Bike> bikeRepository = Substitute.For<ISynchronizableRepository<Bike>>();
    private readonly ISessionPersistenceTransactionRunner sessionPersistenceTransactions = Substitute.For<ISessionPersistenceTransactionRunner>();
    private readonly ITrackCoordinator trackCoordinator = TestCoordinatorSubstitutes.Track();
    private readonly ISessionPreferences sessionPreferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IRecordedSessionSourceStoreWriter sourceStore = Substitute.For<IRecordedSessionSourceStoreWriter>();
    private readonly IRecordedSessionDomainQuery domainQuery = Substitute.For<IRecordedSessionDomainQuery>();
    private readonly IRecordedSessionReprocessor reprocessor = Substitute.For<IRecordedSessionReprocessor>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = new InlineBackgroundTaskRunner();
    private readonly IEditorFactory editorFactory = Substitute.For<IEditorFactory>();
    private readonly ISessionRecomputeEngine recomputeEngine = Substitute.For<ISessionRecomputeEngine>();
    private readonly IRecordedSessionDerivationWindowCache derivationWindowCache = Substitute.For<IRecordedSessionDerivationWindowCache>();
    private readonly IRecordedSessionDerivationWindowProvider derivationWindowProvider = Substitute.For<IRecordedSessionDerivationWindowProvider>();

    public SessionCoordinatorTests()
    {
        sessionPreferences.GetRecordedAsync(Arg.Any<Guid>())
            .Returns(Task.FromResult(SessionPreferences.Default));
        sessionPreferences.RemoveRecordedAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);
        sessionPreferences.UpdateRecordedAsync(Arg.Any<Guid>(), Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .Returns(Task.CompletedTask);
        editorFactory.CloseSessionDetail(Arg.Any<Guid>()).Returns(Task.CompletedTask);
        derivationWindowProvider.IsRecordingSourceReferencedAsync(Arg.Any<Guid>())
            .Returns(Task.FromResult(false));
        sessionPersistenceTransactions.DeleteSessionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task OpenEditAsync_OpensSessionDetail_ThroughFactory()
    {
        var snapshot = TestSnapshots.Session();
        sessionStore.Get(snapshot.Id).Returns(snapshot);

        await CreateCoordinator().OpenEditAsync(snapshot.Id);

        editorFactory.Received(1).OpenSessionDetail(snapshot);
    }

    [Fact]
    public async Task SaveAsync_HappyPath_CommitsThroughStore()
    {
        var existing = TestSnapshots.Session(updated: 5);
        sessionStore.Get(existing.Id).Returns(existing);
        var session = new Session(existing.Id, "renamed", "", null) { Updated = 7 };
        var savedSnapshot = SessionSnapshot.From(new Session(existing.Id, "renamed", "", null)
        {
            Updated = 7,
            HasProcessedData = true,
        });
        sessionStore
            .CommitSessionMetadataAsync(session, 5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoreMutationResult<SessionSnapshot>>(
                new StoreMutationResult<SessionSnapshot>.Saved(savedSnapshot)));

        var result = await CreateCoordinator().SaveAsync(session, baselineUpdated: 5);

        await sessionStore.Received(1).CommitSessionMetadataAsync(session, 5, Arg.Any<CancellationToken>());
        shell.DidNotReceive().GoBack();
        var saved = Assert.IsType<SessionSaveResult.Saved>(result);
        Assert.Equal(7, saved.NewBaselineUpdated);
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
        await sessionStore.DidNotReceive().CommitSessionMetadataAsync(
            Arg.Any<Session>(),
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>());
        shell.DidNotReceive().GoBack();
    }

    [Fact]
    public async Task SaveAsync_ReturnsFailed_WhenCommitFails()
    {
        var existing = TestSnapshots.Session(updated: 5);
        sessionStore.Get(existing.Id).Returns(existing);
        var session = new Session(existing.Id, "x", "", null);
        sessionStore
            .CommitSessionMetadataAsync(session, 5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoreMutationResult<SessionSnapshot>>(
                new StoreMutationResult<SessionSnapshot>.Failed("disk full")));

        var result = await CreateCoordinator().SaveAsync(session, baselineUpdated: 5);

        Assert.IsType<SessionSaveResult.Failed>(result);
        shell.DidNotReceive().GoBack();
    }

    [Fact]
    public async Task SaveLiveCaptureAsync_PersistsTrackSessionAndPublishesFreshSnapshots()
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
            Arg.Is<Session>(saved => saved.Id == session.Id),
            Arg.Is<ProcessedTelemetryPayload>(payload =>
                payload.Data.Length > 0 &&
                payload.FingerprintJson != null),
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
        await sourceStore.Received(1).PublishSourcesChangedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(session.Id)),
            Arg.Any<CancellationToken>());
        await sessionStore.Received(1).PublishSessionsChangedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(session.Id)),
            Arg.Any<CancellationToken>());

        var saved = Assert.IsType<LiveSessionSaveResult.Saved>(result);
        Assert.Equal(session.Id, saved.SessionId);
        Assert.Equal(9, saved.Updated);
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
        await sessionStore.DidNotReceive().PublishSessionsChangedAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
        await sourceStore.DidNotReceive().PublishSourcesChangedAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
    }

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
        sessionStore.CommitDerivedSessionAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                saved = call.Arg<Session>();
                return Task.FromResult<StoreMutationResult<SessionSnapshot>>(
                    new StoreMutationResult<SessionSnapshot>.Saved(SessionSnapshot.From(saved)));
            });

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
        await sessionStore.Received(1).CommitDerivedSessionAsync(
            Arg.Is<Session>(session => session.Id == saved.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_DeletesOrphanedTrack_ClosesAndRemoves()
    {
        var id = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        sessionRepository.GetSessionAsync(id).Returns(new Session(id, "name", "desc", null) { FullTrack = trackId });
        sessionRepository.HasOtherActiveSessionWithFullTrackAsync(trackId, id).Returns(false);

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.Equal(SessionDeleteOutcome.Deleted, result.Outcome);
        await sessionPersistenceTransactions.Received(1).DeleteSessionAsync(
            id,
            trackId,
            deleteFullTrack: true,
            deleteSource: true,
            Arg.Any<CancellationToken>());
        await sourceStore.Received(1).PublishSourcesRemovedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(id)),
            Arg.Any<CancellationToken>());
        await sessionPreferences.Received(1).RemoveRecordedAsync(id);
        await editorFactory.Received(1).CloseSessionDetail(id);
        await sessionStore.Received(1).PublishSessionsRemovedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(id)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadDetailAsync_ReturnsTelemetryBeforeTrackLoad()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var dimensions = new SessionPresentationDimensions(320, 180);

        sessionStore.Get(snapshot.Id).Returns(snapshot);
        SetLocalTelemetry(snapshot.Id, telemetry);
        var progress = new CapturingSessionDetailLoadProgress();

        var result = await CreateCoordinator().LoadDetailAsync(snapshot.Id, dimensions, progress);

        var loaded = Assert.IsType<SessionDetailLoadResult.Loaded>(result);
        Assert.Same(telemetry, loaded.Data.TelemetryPresentation.TelemetryData);
        Assert.Equal(snapshot.FullTrackId, loaded.Data.TelemetryPresentation.FullTrackId);
        Assert.Null(loaded.Data.TelemetryPresentation.FullTrackPoints);
        Assert.Null(loaded.Data.TelemetryPresentation.TrackPoints);
        Assert.Null(loaded.Data.TelemetryPresentation.MediaColumnWidth);
        Assert.Equal(DampingSpeedCutoffs.Default, loaded.Data.TelemetryPresentation.DampingSpeedCutoffs);
        Assert.Null(loaded.Data.TelemetryPresentation.DampingSpeedCutoffOwner);
        await trackCoordinator.DidNotReceive().LoadSessionTrackAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<TelemetryData>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(
            [
                SessionDetailLoadStage.LoadingTelemetryData,
                SessionDetailLoadStage.CheckingLocalData,
            ],
            progress.Reports.Select(report => report.Stage));
    }

    [Fact]
    public async Task LoadTrackAsync_ReturnsTrackData()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var trackData = new SessionTrackPresentationData(
            Guid.NewGuid(),
            [new TrackPoint(1, 1, 1, 0)],
            [new TrackPoint(2, 2, 2, 0)],
            400.0);

        sessionStore.Get(snapshot.Id).Returns(snapshot);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .Returns(trackData);
        var progress = new CapturingSessionDetailLoadProgress();

        var result = await CreateCoordinator().LoadTrackAsync(snapshot.Id, telemetry, progress);

        var loaded = Assert.IsType<SessionDetailTrackLoadResult.Loaded>(result);
        Assert.Same(trackData, loaded.Data);
        Assert.Equal(
            [
                SessionDetailLoadStage.LoadingMapData,
                SessionDetailLoadStage.FinalizingSessionData,
            ],
            progress.Reports.Select(report => report.Stage));
    }

    [Fact]
    public async Task LoadTrackAsync_ReturnsFailed_WhenTrackLoadThrows()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("track failed"));
        var progress = new CapturingSessionDetailLoadProgress();

        var result = await CreateCoordinator().LoadTrackAsync(snapshot.Id, telemetry, progress);

        Assert.IsType<SessionDetailTrackLoadResult.Failed>(result);
        Assert.Equal(
            [SessionDetailLoadStage.LoadingMapData],
            progress.Reports.Select(report => report.Stage));
    }

    [Fact]
    public async Task LoadTrackAsync_PropagatesCancellation()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        trackCoordinator.LoadSessionTrackAsync(snapshot.Id, snapshot.FullTrackId, telemetry, cancellation.Token)
            .Returns(Task.FromCanceled<SessionTrackPresentationData>(cancellation.Token));
        var progress = new CapturingSessionDetailLoadProgress();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateCoordinator().LoadTrackAsync(snapshot.Id, telemetry, progress, cancellation.Token));

        Assert.Equal(
            [SessionDetailLoadStage.LoadingMapData],
            progress.Reports.Select(report => report.Stage));
    }

    [Fact]
    public async Task LoadDetailAsync_ReturnsIncompleteLocalData_WhenTelemetryMissing()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        SetLocalTelemetry(snapshot.Id, null);
        var progress = new CapturingSessionDetailLoadProgress();

        var result = await CreateCoordinator().LoadDetailAsync(
            snapshot.Id,
            new SessionPresentationDimensions(320, 180),
            progress);

        var incomplete = Assert.IsType<SessionDetailLoadResult.IncompleteLocalData>(result);
        Assert.Equal(snapshot.Id, incomplete.SessionId);
        Assert.True(incomplete.Missing.ProcessedTelemetryBlob);
        Assert.False(incomplete.Missing.RecordedSourceMissingOrHashMismatch);
        Assert.Equal(
            [
                SessionDetailLoadStage.LoadingTelemetryData,
                SessionDetailLoadStage.CheckingLocalData,
            ],
            progress.Reports.Select(report => report.Stage));
        await trackCoordinator.DidNotReceive().LoadSessionTrackAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<TelemetryData>(),
            Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task SyncApplier_PublishesOnSessionDataArrived()
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        _ = CreateSyncApplier(sync);
        var sessionId = Guid.NewGuid();

        sync.SessionDataArrived += Raise.EventWith(sync, new SessionDataArrivedEventArgs(sessionId));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        await sessionStore.Received(1).PublishSessionsChangedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(sessionId)),
            Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task SyncApplier_PublishesDeletedAndLiveSessions()
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        _ = CreateSyncApplier(sync);
        var liveId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
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

        await sessionStore.Received(1).PublishSessionsRemovedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(deletedId)),
            Arg.Any<CancellationToken>());
        await sessionStore.Received(1).PublishSessionsChangedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(liveId)),
            Arg.Any<CancellationToken>());
    }

    private SessionLoader CreateLoader() =>
        new(
            sessionStore,
            processedTelemetryReader,
            trackCoordinator,
            domainQuery);

    private SessionCoordinator CreateCoordinator(UiLayoutProfile layoutProfile = UiLayoutProfile.Workspace) =>
        new(
            sessionStore,
            CreateLoader(),
            CreateCommandService(layoutProfile),
            () => editorFactory);

    private SessionCommandService CreateCommandService(UiLayoutProfile layoutProfile = UiLayoutProfile.Workspace) =>
        new(
            sessionStore,
            sessionRepository,
            sessionTelemetryWriter,
            setupRepository,
            bikeRepository,
            sourceStore,
            reprocessor,
            backgroundTaskRunner,
            sessionPreferences,
            shell,
            CreateEnvironment(layoutProfile),
            recomputeEngine,
            () => editorFactory,
            derivationWindowCache,
            derivationWindowProvider,
            sessionPersistenceTransactions);

    private SessionSyncApplier CreateSyncApplier(ISynchronizationServerService? sync = null) =>
        new(
            sessionStore,
            sourceStore,
            sync);

    private void SetLocalTelemetry(Guid sessionId, TelemetryData? telemetry)
    {
        processedTelemetryReader.Set(sessionId, telemetry);
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

    private sealed class CapturingSessionDetailLoadProgress : IProgress<SessionDetailLoadProgress>
    {
        public List<SessionDetailLoadProgress> Reports { get; } = [];

        public void Report(SessionDetailLoadProgress value)
        {
            Reports.Add(value);
        }
    }
}
