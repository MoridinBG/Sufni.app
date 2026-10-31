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

namespace Sufni.App.Tests.Sessions.Coordination;

public class SessionCoordinatorTests
{
    private readonly ITrackStoreWriter trackStore = Substitute.For<ITrackStoreWriter>();
    private readonly StorePublication publication = new(new InlineUiThreadDispatcher());
    private readonly TestSessionProcessedTelemetryReader processedTelemetryReader = new();
    private readonly InMemorySynchronizableRepository<Track> trackRepository = new(new FakeTimeProvider());
    private readonly ISessionStoreWriter sessionStore = Substitute.For<ISessionStoreWriter>();
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly Sufni.App.MapsAndTracks.Services.ITrackRepository trackQueries = Substitute.For<Sufni.App.MapsAndTracks.Services.ITrackRepository>();
    private readonly ISynchronizableRepository<Setup> setupRepository = Substitute.For<ISynchronizableRepository<Setup>>();
    private readonly ISynchronizableRepository<Bike> bikeRepository = Substitute.For<ISynchronizableRepository<Bike>>();
    private readonly ISessionPersistenceTransactionRunner sessionPersistenceTransactions = Substitute.For<ISessionPersistenceTransactionRunner>();
    private readonly ISessionPreferences sessionPreferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
    private readonly IRecordedSessionSourceStoreWriter sourceStore = Substitute.For<IRecordedSessionSourceStoreWriter>();
    private readonly IRecordedSessionReprocessor reprocessor = Substitute.For<IRecordedSessionReprocessor>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = new InlineBackgroundTaskRunner();
    private readonly ISessionRecomputeEngine recomputeEngine = Substitute.For<ISessionRecomputeEngine>();

    public SessionCoordinatorTests()
    {
        sessionPreferences.GetRecordedAsync(Arg.Any<Guid>())
            .Returns(Task.FromResult(SessionPreferences.Default));
        sessionPreferences.RemoveRecordedAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);
        sessionPreferences.UpdateRecordedAsync(Arg.Any<Guid>(), Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .Returns(Task.CompletedTask);
        sessionPersistenceTransactions.DeleteSessionAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(new SessionDeleteCommit(true, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(65.0)]
    public async Task ShiftGpsOffsetAsync_UsesPersistedOffset_AndPublishesTrackMetrics(double? storedDuration)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(20));
        var repository = new InMemorySessionRepository(clock);
        using var store = new SessionStore(publication);
        var original = PrepareAlignment(repository, storedDuration);
        await store.PublishSessionsChangedAsync([original with { GpsOffsetSeconds = 0 }]);

        var result = await CreateCoordinator(store, repository.Repository).ShiftGpsOffsetAsync(original.Id, 1.25, TestContext.Current.CancellationToken);

        Assert.IsType<GpsOffsetResult.Applied>(result);
        var saved = (await repository.Repository.GetSessionAsync(original.Id))!;
        Assert.Equal(2.5, saved.GpsOffsetSeconds);
        Assert.Equal(storedDuration ?? 3, saved.DurationSeconds);
        Assert.True(saved.DistanceMeters > 0);
        Assert.Equal(20, saved.Updated);
        Assert.Equal(SessionSnapshot.From(saved), store.Get(original.Id));
        var points = await repository.Repository.GetSessionTrackAsync(original.Id);
        Assert.Equal(original.Timestamp!.Value + 2.5, points![0].Time);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShiftGpsOffsetAsync_DistinguishesMissingSessionFromPersistenceFailure(bool missing)
    {
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var store = new SessionStore(publication);
        var original = PrepareAlignment(repository, 3);
        await store.PublishSessionsChangedAsync([original]);
        if (missing) repository.Repository.GetSessionAsync(original.Id).Returns((Session?)null);
        else repository.WriteFailure = new IOException("disk full");

        var result = await CreateCoordinator(store, repository.Repository).ShiftGpsOffsetAsync(original.Id, 1.25, TestContext.Current.CancellationToken);

        if (missing) Assert.IsType<GpsOffsetResult.SessionMissing>(result);
        else Assert.Equal(GpsOffsetFailureReason.Save, Assert.IsType<GpsOffsetResult.Failed>(result).Reason);
        Assert.Equal(original, store.Get(original.Id));
    }

    [Fact]
    public async Task ShiftGpsOffsetAsync_LeavesSessionUnchanged_WhenNoTrackSegmentMatches()
    {
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var store = new SessionStore(publication);
        var original = PrepareAlignment(repository, 3);
        await store.PublishSessionsChangedAsync([original]);

        var result = await CreateCoordinator(store, repository.Repository).ShiftGpsOffsetAsync(original.Id, 100, TestContext.Current.CancellationToken);

        Assert.IsType<GpsOffsetResult.NoMatchingTrack>(result);
        Assert.Equal(original, store.Get(original.Id));
        Assert.Equal(original, SessionSnapshot.From((await repository.Repository.GetSessionAsync(original.Id))!));
        Assert.Null(await repository.Repository.GetSessionTrackAsync(original.Id));
    }

    private SessionSnapshot PrepareAlignment(InMemorySessionRepository repository, double? storedDuration)
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.Metadata.Duration = 3;
        var track = new Track
        {
            Id = Guid.NewGuid(),
            Points = Enumerable.Range(0, 7).Select(index => new TrackPoint(telemetry.Metadata.Timestamp + index, index, index, 100)).ToList()
        };
        trackRepository.Seed(track);
        var snapshot = TestSnapshots.Session(hasProcessedData: true, updated: 5, timestamp: telemetry.Metadata.Timestamp)
            with { FullTrackId = track.Id, GpsOffsetSeconds = 1.25, DurationSeconds = storedDuration };
        repository.Seed(snapshot);
        processedTelemetryReader.Set(snapshot.Id, telemetry);
        return snapshot;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAsync_PublishesPersistedMetadata_OnlyWhenPersistenceSucceeds(bool fail)
    {
        var existing = TestSnapshots.Session(updated: 5) with { HasProcessedData = true, DurationSeconds = 120, VelocityFilterWindowMilliseconds = 100 };
        var repository = new InMemorySessionRepository(new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(7)));
        repository.Seed(existing);
        if (fail) repository.WriteFailure = new IOException("disk full");
        using var store = new SessionStore(publication);
        await store.ReplaceAllAsync([existing]);
        var session = new Session(existing.Id, "renamed", "", null);

        var result = await CreateCoordinator(store, repository.Repository).SaveAsync(session, 5);

        var persisted = (await repository.Repository.GetSessionAsync(existing.Id))!;
        if (fail)
        {
            Assert.Equal(SessionSaveFailureReason.Save, Assert.IsType<SessionSaveResult.Failed>(result).Reason);
            Assert.Equal(existing, store.Get(existing.Id));
            Assert.Equal(existing, SessionSnapshot.From(persisted));
        }
        else
        {
            Assert.Equal(7, Assert.IsType<SessionSaveResult.Saved>(result).NewBaselineUpdated);
            Assert.Equal("renamed", persisted.Name);
            Assert.Equal(120, persisted.DurationSeconds);
            Assert.Equal(100, persisted.VelocityFilterWindowMilliseconds);
            Assert.True(persisted.HasProcessedData);
            Assert.Equal(SessionSnapshot.From(persisted), store.Get(existing.Id));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetProcessingOptionAsync_PublishesTheCommittedOption_AndRestoresItOnProcessingFailure(bool fails)
    {
        var clock = new FakeTimeProvider();
        var repository = new InMemorySessionRepository(clock);
        using var store = new SessionStore(publication);
        var original = TestSnapshots.Session(hasProcessedData: true) with { VelocityFilterWindowMilliseconds = 50 };
        repository.Seed(original);
        await store.PublishSessionsChangedAsync([original]);
        int? optionSeenDuringRecompute = null;
        recomputeEngine.RequestRecomputeAsync(original.Id, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                optionSeenDuringRecompute = store.Get(original.Id)!.VelocityFilterWindowMilliseconds;
                return Task.FromResult<SessionRecomputeResult>(fails
                    ? new SessionRecomputeResult.ProcessingFailed(SessionProcessingFailureReason.NoTravelData)
                    : new SessionRecomputeResult.Recomputed(1));
            });

        var result = await CreateCoordinator(store, repository.Repository).SetProcessingOptionAsync(original.Id, 100);

        Assert.Equal(100, optionSeenDuringRecompute);
        Assert.Equal(fails ? 50 : 100, store.Get(original.Id)!.VelocityFilterWindowMilliseconds);
        Assert.Equal(store.Get(original.Id)!.VelocityFilterWindowMilliseconds,
            (await repository.Repository.GetSessionAsync(original.Id))!.VelocityFilterWindowMilliseconds);
        Assert.True(store.Get(original.Id)!.HasProcessedData);
        if (fails) Assert.IsType<SessionRecomputeResult.ProcessingFailed>(result);
        else Assert.IsType<SessionRecomputeResult.Recomputed>(result);
    }

    [Fact]
    public async Task SetProcessingOptionAsync_DoesNotRestoreOverANewerAuthoredOption()
    {
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var store = new SessionStore(publication);
        var original = TestSnapshots.Session() with { VelocityFilterWindowMilliseconds = 50 };
        repository.Seed(original);
        await store.PublishSessionsChangedAsync([original]);
        recomputeEngine.RequestRecomputeAsync(original.Id, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await repository.Repository.SetProcessingOptionAsync(original.Id, 250);
                return (SessionRecomputeResult)new SessionRecomputeResult.Failed(SessionRecomputeFailureReason.Recompute);
            });

        await CreateCoordinator(store, repository.Repository).SetProcessingOptionAsync(original.Id, 100);

        Assert.Equal(250, store.Get(original.Id)!.VelocityFilterWindowMilliseconds);
        Assert.Equal(250, (await repository.Repository.GetSessionAsync(original.Id))!.VelocityFilterWindowMilliseconds);
    }

    [Fact]
    public async Task SaveAsync_ReturnsConflictWithoutOverwritingNewerState()
    {
        var current = TestSnapshots.Session(updated: 10);
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        repository.Seed(current);
        using var store = new SessionStore(publication);
        await store.ReplaceAllAsync([current]);

        var result = await CreateCoordinator(store, repository.Repository).SaveAsync(new Session(current.Id, "stale", "", null), 5);

        Assert.Equal(current, Assert.IsType<SessionSaveResult.Conflict>(result).CurrentSnapshot);
        Assert.Equal(current, store.Get(current.Id));
        Assert.Equal(current, SessionSnapshot.From((await repository.Repository.GetSessionAsync(current.Id))!));
    }

    [Fact]
    public async Task SaveAsync_WhenSessionDisappearsAfterPersistence_ReturnsMissingAndRemovesPublishedRow()
    {
        var existing = TestSnapshots.Session(updated: 5);
        using var store = new SessionStore(publication);
        await store.ReplaceAllAsync([existing]);
        sessionRepository.GetSessionAsync(existing.Id).Returns((Session?)null);

        var result = await CreateCoordinator(store).SaveAsync(existing.ToMetadataEntity(), 5);

        Assert.IsType<SessionSaveResult.Missing>(result);
        Assert.Null(store.Get(existing.Id));
    }

    [Fact]
    public async Task SaveLiveCaptureAsync_ProcessingFailure_DoesNotPersistOrPublishCapture()
    {
        var capture = CreateLiveCapturePackage(withGps: false);
        SeedLiveCaptureDependencies(capture);
        reprocessor.ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>())
            .Returns(new RecordedSessionReprocessResult.Failed(SessionProcessingFailureReason.NoTravelData));
        using var store = new SessionStore(publication);
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        var session = new Session(Guid.NewGuid(), "capture", "", capture.Context.SetupId);

        var result = await CreateCoordinator(store, repository.Repository).SaveLiveCaptureAsync(
            session, capture, SessionPreferences.Default, TestContext.Current.CancellationToken);

        Assert.Equal(SessionProcessingFailureReason.NoTravelData, Assert.IsType<LiveSessionSaveResult.ProcessingFailed>(result).Reason);
        Assert.Empty(await repository.Repository.GetSessionsAsync());
        Assert.Null(store.Get(RecordedSessionSourceFactory.CreateLiveCapture(capture.TelemetryCapture).SessionId));
    }

    [Fact]
    public async Task SaveLiveCaptureAsync_AssociatesStoredTrack_AndPublishesPreparedGeneration()
    {
        var capture = CreateLiveCapturePackage(withGps: false);
        SeedLiveCaptureDependencies(capture);
        var timestamp = capture.TelemetryCapture.Metadata.Timestamp;
        var track = new Track
        {
            Points = Enumerable.Range(0, 4).Select(index => new TrackPoint(timestamp + index, index, index, 100 + index * 4)).ToList()
        };
        trackRepository.Seed(track);
        trackQueries.FindTrackContainingTimestampAsync(timestamp).Returns(track.Id);
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var store = new SessionStore(publication);
        var session = new Session(Guid.NewGuid(), "ride", "notes", capture.Context.SetupId, timestamp);

        var result = await CreateCoordinator(store, repository.Repository).SaveLiveCaptureAsync(
            session, capture, SessionPreferences.Default, TestContext.Current.CancellationToken);

        var saved = Assert.IsType<LiveSessionSaveResult.Saved>(result);
        var persisted = (await repository.Repository.GetSessionAsync(saved.SessionId))!;
        Assert.Equal(RecordedSessionSourceFactory.CreateLiveCapture(capture.TelemetryCapture).SessionId, saved.SessionId);
        Assert.NotEqual(session.Id, saved.SessionId);
        Assert.Equal(track.Id, persisted.FullTrack);
        Assert.True(persisted.DistanceMeters > 0);
        Assert.Equal(capture.TelemetryCapture.Metadata.Duration, persisted.DurationSeconds);
        Assert.Equal(SessionSnapshot.From(persisted), store.Get(saved.SessionId));
        Assert.Equal(timestamp, (await repository.Repository.GetSessionTrackAsync(saved.SessionId))![0].Time);
        var bytes = await repository.Repository.GetSessionRawPsstAsync(saved.SessionId);
        Assert.NotNull(bytes);
        Assert.Equal(capture.TelemetryCapture.Metadata.Duration, TelemetryData.FromBinary(bytes).Metadata.Duration);
    }

    [Fact]
    public async Task SaveLiveCaptureAsync_PublishesRecordingSourceAndTrackTogether()
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
            sessions.Get(id)?.FullTrackId is { } trackId ? tracks.Get(trackId) : null)));

        var result = await coordinator.SaveLiveCaptureAsync(
            new Session(Guid.NewGuid(), "ride", "notes", capture.Context.SetupId),
            capture, SessionPreferences.Default, TestContext.Current.CancellationToken);

        Assert.IsType<LiveSessionSaveResult.Saved>(result);
        var committed = Assert.Single(observed);
        Assert.Equal(id, committed.Session!.Id);
        Assert.Equal(id, committed.Source!.SessionId);
        Assert.Equal(committed.Session.FullTrackId, committed.Track!.Id);
        Assert.Equal(7, committed.Track.PointsRevision);
    }

    [Fact]
    public async Task SaveLiveCaptureAsync_RepeatedCapture_PreservesExistingRecording()
    {
        var capture = CreateLiveCapturePackage(withGps: false);
        SeedLiveCaptureDependencies(capture);
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var store = new SessionStore(publication);
        using var sources = new RecordedSessionSourceStore(publication);
        var coordinator = CreateCoordinator(store, repository.Repository, sources);
        var original = new Session(Guid.NewGuid(), "original", "notes", capture.Context.SetupId);
        var saved = Assert.IsType<LiveSessionSaveResult.Saved>(await coordinator.SaveLiveCaptureAsync(
            original, capture, SessionPreferences.Default));
        var before = store.Get(saved.SessionId);
        capture.TelemetryCapture.Metadata.SourceName = "renamed setup";

        var repeated = Assert.IsType<LiveSessionSaveResult.Saved>(await coordinator.SaveLiveCaptureAsync(
            new Session(Guid.NewGuid(), "replacement", "other notes", capture.Context.SetupId),
            capture, SessionPreferences.Default));

        Assert.Equal(saved.SessionId, repeated.SessionId);
        Assert.Equal(before, store.Get(repeated.SessionId));
        Assert.Single(await repository.Repository.GetSessionsAsync());
        Assert.Equal("live", sources.Get(saved.SessionId)!.SourceName);
    }

    [Fact]
    public async Task SaveLiveCaptureAsync_PersistsGpsAndPublishesSessionAndSource()
    {
        var capture = CreateLiveCapturePackage(withGps: true);
        SeedLiveCaptureDependencies(capture);
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var sessions = new SessionStore(publication);
        using var sources = new RecordedSessionSourceStore(publication);
        var expectedSource = RecordedSessionSourceFactory.CreateLiveCapture(capture.TelemetryCapture);
        var draft = new Session(Guid.NewGuid(), "live session", "notes", capture.Context.SetupId, capture.TelemetryCapture.Metadata.Timestamp);

        var result = await CreateCoordinator(sessions, repository.Repository, sources).SaveLiveCaptureAsync(
            draft, capture, SessionPreferences.Default, TestContext.Current.CancellationToken);

        var saved = Assert.IsType<LiveSessionSaveResult.Saved>(result);
        Assert.Equal(expectedSource.SessionId, saved.SessionId);
        var persisted = (await repository.Repository.GetSessionAsync(saved.SessionId))!;
        Assert.Equal(SessionSnapshot.From(persisted), sessions.Get(saved.SessionId));
        Assert.Equal(RecordedSessionSourceSnapshot.From(expectedSource), sources.Get(saved.SessionId));
        Assert.NotNull(persisted.FullTrack);
        Assert.NotEmpty((await repository.Repository.GetSessionRawPsstAsync(saved.SessionId))!);
        var point = Assert.Single((await repository.Repository.GetSessionTrackAsync(saved.SessionId))!);
        Assert.Equal((byte?)3, point.FixMode);
        Assert.Equal((byte?)12, point.Satellites);
        Assert.Equal(0.5f, point.Epe2d);
        Assert.Equal(0.8f, point.Epe3d);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveLiveCaptureAsync_ReturnsSavedWithWarning_WhenPostCommitPublicationFails(bool sourcePublicationFails)
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

        var result = await coordinator.SaveLiveCaptureAsync(draft, capture, SessionPreferences.Default, TestContext.Current.CancellationToken);

        var saved = Assert.IsType<LiveSessionSaveResult.Saved>(result);
        Assert.NotNull(saved.PublicationWarning);
        Assert.Equal("notes", (await repository.Repository.GetSessionAsync(saved.SessionId))!.Description);
        Assert.NotEmpty((await repository.Repository.GetSessionRawPsstAsync(saved.SessionId))!);
    }

    [Fact]
    public async Task SaveLiveCaptureAsync_ReturnsFailed_WhenProcessedPersistenceThrows()
    {
        var capture = CreateLiveCapturePackage(withGps: false);
        SeedLiveCaptureDependencies(capture);
        var repository = new InMemorySessionRepository(new FakeTimeProvider()) { WriteFailure = new IOException("disk full") };
        using var sessions = new SessionStore(publication);
        using var sources = new RecordedSessionSourceStore(publication);
        var draft = new Session(Guid.NewGuid(), "ride", "notes", capture.Context.SetupId, capture.TelemetryCapture.Metadata.Timestamp);

        var result = await CreateCoordinator(sessions, repository.Repository, sources).SaveLiveCaptureAsync(
            draft, capture, SessionPreferences.Default, TestContext.Current.CancellationToken);

        Assert.IsType<LiveSessionSaveResult.Failed>(result);
        Assert.Empty(await repository.Repository.GetSessionsAsync());
        var id = RecordedSessionSourceFactory.CreateLiveCapture(capture.TelemetryCapture).SessionId;
        Assert.Null(sessions.Get(id));
        Assert.Null(sources.Get(id));
    }

    [Fact]
    public async Task DeleteAsync_WhenTransactionFails_KeepsSessionAndPreferences()
    {
        var original = TestSnapshots.Session();
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        repository.Seed(original);
        using var store = new SessionStore(publication);
        await store.ReplaceAllAsync([original]);
        sessionPersistenceTransactions.DeleteSessionAsync(original.Id, Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("database diagnostic"));

        var result = await CreateCoordinator(store, repository.Repository).DeleteAsync(original.Id);

        Assert.Equal(SessionDeleteFailureReason.Delete, Assert.IsType<SessionDeleteResult.Failed>(result).Reason);
        Assert.Equal(original, store.Get(original.Id));
        Assert.Equal(original, SessionSnapshot.From((await repository.Repository.GetSessionAsync(original.Id))!));
        await sessionPreferences.DidNotReceive().RemoveRecordedAsync(original.Id);
    }

    [Fact]
    public async Task DeleteAsync_PublishesRemovalAndClearsPreferencesAfterCommit()
    {
        var id = Guid.NewGuid();
        var track = new TrackSnapshot(Guid.NewGuid(), 100, 101, 1);
        using var tracks = new TrackStore(publication);
        await tracks.PublishTracksChangedAsync([track]);
        sessionPersistenceTransactions.DeleteSessionAsync(id, Arg.Any<CancellationToken>())
            .Returns(new SessionDeleteCommit(true, track.Id));
        using var store = new SessionStore(publication);
        using var sources = new RecordedSessionSourceStore(publication);
        await store.PublishSessionsChangedAsync([TestSnapshots.Session(id: id)]);
        await sources.PublishSourcesChangedAsync([RecordedSessionSourceSnapshot.From(PersistenceTestData.CreateRecordedSessionSource(id))]);
        var observed = new List<(SessionSnapshot? Session, RecordedSessionSourceSnapshot? Source, TrackSnapshot? Track)>();
        using var subscription = publication.Committed.Subscribe(_ => observed.Add((store.Get(id), sources.Get(id), tracks.Get(track.Id))));

        var result = await CreateCoordinator(store, sources: sources, tracks: tracks).DeleteAsync(id);

        Assert.IsType<SessionDeleteResult.Deleted>(result);
        await sessionPersistenceTransactions.Received(1).DeleteSessionAsync(id, Arg.Any<CancellationToken>());
        await sessionPreferences.Received(1).RemoveRecordedAsync(id);
        var removed = Assert.Single(observed);
        Assert.Null(removed.Session);
        Assert.Null(removed.Source);
        Assert.Null(removed.Track);
    }

    [Fact]
    public async Task DeleteAsync_WhenSourceIsKept_DoesNotRemoveSource()
    {
        var id = Guid.NewGuid();
        sessionRepository.GetSessionAsync(id).Returns(new Session(id, "name", "desc", null));
        sessionPersistenceTransactions.DeleteSessionAsync(id, Arg.Any<CancellationToken>())
            .Returns(new SessionDeleteCommit(false, null));

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.IsType<SessionDeleteResult.Deleted>(result);
        await sourceStore.DidNotReceive().PublishSourcesRemovedAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
    }

    private SessionCoordinator CreateCoordinator(ISessionStoreWriter? store = null, ISessionRepository? repository = null,
        IRecordedSessionSourceStoreWriter? sources = null, ITrackStoreWriter? tracks = null) =>
        new(
            store ?? sessionStore,
            repository ?? sessionRepository,
            trackQueries,
            setupRepository,
            bikeRepository,
            sources ?? sourceStore,
            reprocessor,
            backgroundTaskRunner,
            sessionPreferences,
            recomputeEngine,
            sessionPersistenceTransactions,
            new InlineUiThreadDispatcher(),
            processedTelemetryReader,
            trackRepository,
            publication,
            tracks ?? trackStore);

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
