using Sufni.App.Sessions.Services.Preferences;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Shared.Models;
using Sufni.App.Tests.TestSupport.Async;
using NSubstitute;
using System.Reactive.Linq;
using Microsoft.Extensions.Time.Testing;
using Sufni.App.Tests.TestSupport.Persistence;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
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

using Sufni.App.LiveDaq.Coordinators;

namespace Sufni.App.Tests.LiveDaq.Coordinators;

public partial class LiveDaqCoordinatorTests
{
    [Fact]
    public async Task SaveCaptureAsync_PreparationFailureRetainsCaptureWithoutPersistence()
    {
        var service = Substitute.For<ILiveSessionService>();
        service.PrepareCaptureForSaveAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("capture diagnostic"));
        var draft = SessionSnapshot.From(new Session(Guid.NewGuid(), "ride", "", null));

        var result = await CreateCoordinator().SaveCaptureAsync(draft, service,
            SessionPreferences.Default, TestContext.Current.CancellationToken);

        Assert.IsType<LiveSessionSaveResult.Failed>(result);
        await sessionRepository.DidNotReceive().ImportProcessedSessionAsync(
            Arg.Any<Session>(), Arg.Any<Track?>(), Arg.Any<RecordedSessionSource>());
        await service.DidNotReceive().ResetCaptureAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveCaptureAsync_PreferenceFailureStillPublishesAndResetsCommittedCapture()
    {
        var capture = CreateLiveCapturePackage(false);
        SeedLiveCaptureDependencies(capture);
        var service = CreateCaptureService(capture);
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var sessions = new SessionStore(publication);
        sessionPreferences.UpdateRecordedAsync(Arg.Any<Guid>(), Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .ThrowsAsync(new IOException("preference diagnostic"));

        var saved = Assert.IsType<LiveSessionSaveResult.Saved>(await CreateCoordinator(sessions, repository.Repository)
            .SaveCaptureAsync(SessionSnapshot.From(new Session(Guid.NewGuid(), "ride", "", capture.Context.SetupId)),
                service, SessionPreferences.Default, TestContext.Current.CancellationToken));

        Assert.Equal("preference diagnostic", saved.PreferenceWarning);
        Assert.NotNull(sessions.Get(saved.SessionId));
        Assert.NotNull(await repository.Repository.GetSessionAsync(saved.SessionId));
        await service.Received(1).ResetCaptureAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveCaptureAsync_AwaitsResetAndReportsCommittedRecording_WhenResetFails()
    {
        var capture = CreateLiveCapturePackage(false) with { CaptureRevision = 17 };
        SeedLiveCaptureDependencies(capture);
        var service = CreateCaptureService(capture);
        var resetStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reset = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ResetCaptureAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            resetStarted.SetResult();
            return reset.Task;
        });
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var sessions = new SessionStore(publication);
        var draft = SessionSnapshot.From(new Session(Guid.NewGuid(), "ride", "notes", capture.Context.SetupId));

        var save = CreateCoordinator(sessions, repository.Repository).SaveCaptureAsync(
            draft, service, SessionPreferences.Default, TestContext.Current.CancellationToken);
        await resetStarted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.False(save.IsCompleted);
        var committed = Assert.Single(await repository.Repository.GetSessionsAsync());
        Assert.NotNull(sessions.Get(committed.Id));
        reset.SetException(new IOException("reset diagnostic"));
        var saved = Assert.IsType<LiveSessionSaveResult.Saved>(await save.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(committed.Id, saved.SessionId);
        Assert.Equal(capture.CaptureRevision, saved.CaptureRevision);
        Assert.Equal("reset diagnostic", saved.CleanupFailure);
        Assert.Equal(capture.TelemetryCapture.Metadata.Timestamp, committed.Timestamp);
        Assert.Null(draft.Timestamp);
    }

    private readonly ITrackStoreWriter trackStore = Substitute.For<ITrackStoreWriter>();
    private readonly StorePublication publication = new(new InlineUiThreadDispatcher());
    private readonly ISessionStoreWriter sessionStore = Substitute.For<ISessionStoreWriter>();
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ITrackRepository trackQueries = Substitute.For<ITrackRepository>();
    private readonly ISynchronizableRepository<Setup> setupRepository = Substitute.For<ISynchronizableRepository<Setup>>();
    private readonly ISynchronizableRepository<Bike> bikeRepository = Substitute.For<ISynchronizableRepository<Bike>>();
    private readonly IRecordedSessionSourceStoreWriter sourceStore = Substitute.For<IRecordedSessionSourceStoreWriter>();
    private readonly IRecordedSessionReprocessor reprocessor = Substitute.For<IRecordedSessionReprocessor>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = new InlineBackgroundTaskRunner();
    private readonly ISessionPreferences sessionPreferences = Substitute.For<ISessionPreferences>();

    private static ILiveSessionService CreateCaptureService(LiveSessionCapturePackage capture)
    {
        var service = Substitute.For<ILiveSessionService>();
        service.PrepareCaptureForSaveAsync(Arg.Any<CancellationToken>()).Returns(capture);
        return service;
    }

    [Fact]
    public async Task SaveCaptureAsync_ProcessingFailure_DoesNotPersistOrPublishCapture()
    {
        var capture = CreateLiveCapturePackage(withGps: false);
        SeedLiveCaptureDependencies(capture);
        reprocessor.ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>())
            .Returns(new RecordedSessionReprocessResult.Failed(SessionProcessingFailureReason.NoTravelData));
        using var store = new SessionStore(publication);
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        var session = new Session(Guid.NewGuid(), "capture", "", capture.Context.SetupId);

        var result = await CreateCoordinator(store, repository.Repository).SaveCaptureAsync(SessionSnapshot.From(session), CreateCaptureService(capture), SessionPreferences.Default, TestContext.Current.CancellationToken);

        Assert.Equal(SessionProcessingFailureReason.NoTravelData, Assert.IsType<LiveSessionSaveResult.ProcessingFailed>(result).Reason);
        Assert.Empty(await repository.Repository.GetSessionsAsync());
        Assert.Null(store.Get(RecordedSessionSourceFactory.CreateLiveCapture(capture.TelemetryCapture).SessionId));
    }

    [Fact]
    public async Task SaveCaptureAsync_PublishesPreparedGeneration_WithoutGps()
    {
        var capture = CreateLiveCapturePackage(withGps: false);
        SeedLiveCaptureDependencies(capture);
        var timestamp = capture.TelemetryCapture.Metadata.Timestamp;
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var store = new SessionStore(publication);
        var session = new Session(Guid.NewGuid(), "ride", "notes", capture.Context.SetupId, timestamp);

        var result = await CreateCoordinator(store, repository.Repository).SaveCaptureAsync(SessionSnapshot.From(session), CreateCaptureService(capture), SessionPreferences.Default, TestContext.Current.CancellationToken);

        var saved = Assert.IsType<LiveSessionSaveResult.Saved>(result);
        var persisted = (await repository.Repository.GetSessionAsync(saved.SessionId))!;
        Assert.Equal(RecordedSessionSourceFactory.CreateLiveCapture(capture.TelemetryCapture).SessionId, saved.SessionId);
        Assert.NotEqual(session.Id, saved.SessionId);

        Assert.Null(persisted.DistanceMeters);
        Assert.Equal(capture.TelemetryCapture.Metadata.Duration, persisted.DurationSeconds);
        Assert.Equal(SessionSnapshot.From(persisted), store.Get(saved.SessionId));
        var bytes = await repository.Repository.GetSessionRawPsstAsync(saved.SessionId);
        Assert.NotNull(bytes);
        Assert.Equal(capture.TelemetryCapture.Metadata.Duration, TelemetryData.FromBinary(bytes).Metadata.Duration);
    }

    [Fact]
    public async Task SaveCaptureAsync_PublishesRecordingSourceAndTrackTogether()
    {
        var capture = CreateLiveCapturePackage(withGps: true);
        SeedLiveCaptureDependencies(capture);
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var sessions = new SessionStore(publication);
        using var sources = new RecordedSessionSourceStore(publication);
        using var tracks = new TrackStore(publication);
        trackQueries.GetTrackSnapshotsAsync(Arg.Any<IReadOnlyCollection<Guid>>()).Returns(call =>
            new[] { new TrackSnapshot(call.Arg<IReadOnlyCollection<Guid>>().Single(), 100, 101, 7) });
        var coordinator = CreateCoordinator(sessions, repository.Repository, sources, tracks);
        var id = RecordedSessionSourceFactory.CreateLiveCapture(capture.TelemetryCapture).SessionId;
        var observed = new List<(SessionSnapshot? Session, RecordedSessionSourceSnapshot? Source, TrackSnapshot? Track)>();
        using var subscription = publication.Committed.Subscribe(_ => observed.Add((sessions.Get(id), sources.Get(id),
            tracks.GetAll().SingleOrDefault())));

        var result = await coordinator.SaveCaptureAsync(SessionSnapshot.From(new Session(Guid.NewGuid(), "ride", "notes", capture.Context.SetupId)), CreateCaptureService(capture), SessionPreferences.Default, TestContext.Current.CancellationToken);

        Assert.IsType<LiveSessionSaveResult.Saved>(result);
        var committed = Assert.Single(observed);
        Assert.Equal(id, committed.Session!.Id);
        Assert.Equal(id, committed.Source!.SessionId);
        Assert.NotNull(committed.Track);
        Assert.Equal(7, committed.Track.PointsRevision);
    }

    [Fact]
    public async Task SaveCaptureAsync_RepeatedCapture_PreservesExistingRecording()
    {
        var capture = CreateLiveCapturePackage(withGps: false);
        SeedLiveCaptureDependencies(capture);
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var store = new SessionStore(publication);
        using var sources = new RecordedSessionSourceStore(publication);
        var coordinator = CreateCoordinator(store, repository.Repository, sources);
        var original = new Session(Guid.NewGuid(), "original", "notes", capture.Context.SetupId);
        var saved = Assert.IsType<LiveSessionSaveResult.Saved>(await coordinator.SaveCaptureAsync(SessionSnapshot.From(original), CreateCaptureService(capture), SessionPreferences.Default));
        var before = store.Get(saved.SessionId);
        capture = capture with { TelemetryCapture = capture.TelemetryCapture with { Metadata = capture.TelemetryCapture.Metadata with { SourceName = "renamed setup" } } };

        var repeated = Assert.IsType<LiveSessionSaveResult.Saved>(await coordinator.SaveCaptureAsync(SessionSnapshot.From(new Session(Guid.NewGuid(), "replacement", "other notes", capture.Context.SetupId)), CreateCaptureService(capture), SessionPreferences.Default));

        Assert.Equal(saved.SessionId, repeated.SessionId);
        Assert.Equal(before, store.Get(repeated.SessionId));
        Assert.Single(await repository.Repository.GetSessionsAsync());
        Assert.Equal("live", sources.Get(saved.SessionId)!.SourceName);
    }

    [Fact]
    public async Task SaveCaptureAsync_PassesPreparedGpsTrackToAtomicCommit()
    {
        var capture = CreateLiveCapturePackage(withGps: true);
        SeedLiveCaptureDependencies(capture);
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var sessions = new SessionStore(publication);
        using var sources = new RecordedSessionSourceStore(publication);
        var expectedSource = RecordedSessionSourceFactory.CreateLiveCapture(capture.TelemetryCapture);
        var draft = new Session(Guid.NewGuid(), "live session", "notes", capture.Context.SetupId, capture.TelemetryCapture.Metadata.Timestamp);

        var result = await CreateCoordinator(sessions, repository.Repository, sources).SaveCaptureAsync(SessionSnapshot.From(draft), CreateCaptureService(capture), SessionPreferences.Default, TestContext.Current.CancellationToken);

        var saved = Assert.IsType<LiveSessionSaveResult.Saved>(result);
        Assert.Equal(expectedSource.SessionId, saved.SessionId);
        var persisted = (await repository.Repository.GetSessionAsync(saved.SessionId))!;
        Assert.Equal(SessionSnapshot.From(persisted), sessions.Get(saved.SessionId));
        Assert.Equal(RecordedSessionSourceSnapshot.From(expectedSource), sources.Get(saved.SessionId));

        await repository.Repository.Received(1).ImportProcessedSessionAsync(Arg.Any<Session>(),
            Arg.Is<Track>(track => track.Points.Count == 1 && track.Points[0].FixMode == 3 &&
                track.Points[0].Satellites == 12 && track.Points[0].Epe2d == 0.5f && track.Points[0].Epe3d == 0.8f),
            Arg.Any<RecordedSessionSource>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveCaptureAsync_ReturnsSavedWithWarning_WhenPostCommitPublicationFails(bool sourcePublicationFails)
    {
        var capture = CreateLiveCapturePackage(withGps: false);
        SeedLiveCaptureDependencies(capture);
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var sessions = new SessionStore(publication);
        using var sources = new RecordedSessionSourceStore(publication);
        if (sourcePublicationFails)
            sourceStore.PublishSourcesChangedAsync(Arg.Any<IReadOnlyCollection<RecordedSessionSourceSnapshot>>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new IOException("source publication failed"));
        else
            sessionStore.PublishSessionsChangedAsync(Arg.Any<IReadOnlyCollection<SessionSnapshot>>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new IOException("session publication failed"));
        var coordinator = CreateCoordinator(sourcePublicationFails ? sessions : sessionStore, repository.Repository,
            sourcePublicationFails ? sourceStore : sources);
        var draft = new Session(Guid.NewGuid(), "ride", "notes", capture.Context.SetupId, capture.TelemetryCapture.Metadata.Timestamp);

        var result = await coordinator.SaveCaptureAsync(SessionSnapshot.From(draft), CreateCaptureService(capture), SessionPreferences.Default, TestContext.Current.CancellationToken);

        var saved = Assert.IsType<LiveSessionSaveResult.Saved>(result);
        Assert.NotNull(saved.PublicationWarning);
        Assert.Equal("notes", (await repository.Repository.GetSessionAsync(saved.SessionId))!.Description);
        Assert.NotEmpty((await repository.Repository.GetSessionRawPsstAsync(saved.SessionId))!);
    }

    [Fact]
    public async Task SaveCaptureAsync_ReturnsFailed_WhenProcessedPersistenceThrows()
    {
        var capture = CreateLiveCapturePackage(withGps: false);
        SeedLiveCaptureDependencies(capture);
        var repository = new InMemorySessionRepository(new FakeTimeProvider()) { WriteFailure = new IOException("disk full") };
        using var sessions = new SessionStore(publication);
        using var sources = new RecordedSessionSourceStore(publication);
        var draft = new Session(Guid.NewGuid(), "ride", "notes", capture.Context.SetupId, capture.TelemetryCapture.Metadata.Timestamp);

        var result = await CreateCoordinator(sessions, repository.Repository, sources).SaveCaptureAsync(SessionSnapshot.From(draft), CreateCaptureService(capture), SessionPreferences.Default, TestContext.Current.CancellationToken);

        Assert.IsType<LiveSessionSaveResult.Failed>(result);
        Assert.Empty(await repository.Repository.GetSessionsAsync());
        var id = RecordedSessionSourceFactory.CreateLiveCapture(capture.TelemetryCapture).SessionId;
        Assert.Null(sessions.Get(id));
        Assert.Null(sources.Get(id));
    }

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
            DampingSpeedCutoffs: DampingSpeedCutoffs.Default);

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
                FrontSegments: [new RawCountSegment { Counts = [1000, 1010, 1020, 1030, 1040, 1050] }],
                RearSegments: [new RawCountSegment { Counts = [1100, 1110, 1120, 1130, 1140, 1150] }],
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
                Markers: [],
                StreamGaps: [],
                FinalStatus: null,
                MissingFinalStatus: false));
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
            .ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var domain = callInfo.ArgAt<RecordedSessionDerivationInput>(0);
                var source = domain.Source;
                var processingOptions = callInfo.ArgAt<RecordedSessionDerivationInput>(0).ProcessingOptions;
                var telemetryData = TelemetryData.FromLiveCapture(capture.TelemetryCapture, processingOptions);
                var trackPoints = telemetryData.GpsData is { Length: > 0 }
                    ? GpsTrackPointProjection.ProjectAll(telemetryData.GpsData)
                    : null;
                var fingerprint = new ProcessingFingerprint(
                    SchemaVersion: 2,
                    ProcessingVersion: 1,
                    SetupId: domain.Setup!.Id,
                    BikeId: domain.Bike!.Id,
                    TrackProjectionVersion: 1,
                    DependencyHash: "dependency",
                    SourceHash: source.SourceHash);
                return Task.FromResult<RecordedSessionReprocessResult>(new RecordedSessionReprocessResult.Processed(
                    ProcessedGeneration.Create(new ProcessedTelemetryPayload(
                        telemetryData,
                        telemetryData.BinaryForm,
                        AppJson.Serialize(fingerprint)), trackPoints),
                    fingerprint));
            });
    }

}
