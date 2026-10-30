using Sufni.App.Shared.Models;
using Sufni.App.Tests.TestSupport.Async;
using NSubstitute;
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
    private readonly ISessionStoreWriter sessionStore = Substitute.For<ISessionStoreWriter>();
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ISessionTelemetryWriter sessionTelemetryWriter = Substitute.For<ISessionTelemetryWriter>();
    private readonly TestSessionProcessedTelemetryReader processedTelemetryReader = new();
    private readonly ISynchronizableRepository<Setup> setupRepository = Substitute.For<ISynchronizableRepository<Setup>>();
    private readonly ISynchronizableRepository<Bike> bikeRepository = Substitute.For<ISynchronizableRepository<Bike>>();
    private readonly ISessionPersistenceTransactionRunner sessionPersistenceTransactions = Substitute.For<ISessionPersistenceTransactionRunner>();
    private readonly ITrackCoordinator trackCoordinator = TestCoordinatorSubstitutes.Track();
    private readonly ISessionPreferences sessionPreferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
    private readonly IRecordedSessionSourceStoreWriter sourceStore = Substitute.For<IRecordedSessionSourceStoreWriter>();
    private readonly IRecordedSessionDomainQuery domainQuery = Substitute.For<IRecordedSessionDomainQuery>();
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
                Arg.Any<Guid?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
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
        using var store = new SessionStore(new StorePublication(new InlineUiThreadDispatcher()));
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
        using var store = new SessionStore(new StorePublication(new InlineUiThreadDispatcher()));
        var original = TestSnapshots.Session(hasProcessedData: true) with { VelocityFilterWindowMilliseconds = 50 };
        repository.Seed(original);
        await store.PublishSessionsChangedAsync([original]);
        int? optionSeenDuringRecompute = null;
        recomputeEngine.RequestRecomputeAsync(original.Id, RecomputeReason.ProcessingPreferenceChanged, Arg.Any<CancellationToken>())
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
        using var store = new SessionStore(new StorePublication(new InlineUiThreadDispatcher()));
        var original = TestSnapshots.Session() with { VelocityFilterWindowMilliseconds = 50 };
        repository.Seed(original);
        await store.PublishSessionsChangedAsync([original]);
        recomputeEngine.RequestRecomputeAsync(original.Id, RecomputeReason.ProcessingPreferenceChanged, Arg.Any<CancellationToken>())
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
        using var store = new SessionStore(new StorePublication(new InlineUiThreadDispatcher()));
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
        using var store = new SessionStore(new StorePublication(new InlineUiThreadDispatcher()));
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
        using var store = new SessionStore(new StorePublication(new InlineUiThreadDispatcher()));
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        var session = new Session(Guid.NewGuid(), "capture", "", capture.Context.SetupId);

        var result = await CreateCoordinator(store, repository.Repository).SaveLiveCaptureAsync(
            session, capture, SessionPreferences.Default, TestContext.Current.CancellationToken);

        Assert.Equal(SessionProcessingFailureReason.NoTravelData, Assert.IsType<LiveSessionSaveResult.ProcessingFailed>(result).Reason);
        Assert.Null(store.Get(session.Id));
        Assert.Null(await repository.Repository.GetSessionAsync(session.Id));
        await sessionTelemetryWriter.DidNotReceive().PutProcessedSessionAsync(
            Arg.Any<Session>(), Arg.Any<ProcessedTelemetryPayload>(), Arg.Any<Track?>(), Arg.Any<RecordedSessionSource?>());
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

        var result = await CreateCoordinator().SaveLiveCaptureAsync(session, capture, SessionPreferences.Default, cancellationToken: TestContext.Current.CancellationToken);

        await reprocessor.Received(1).ReprocessAsync(Arg.Is<RecordedSessionDerivationInput>(domain =>
                domain.Session.Id == session.Id &&
                domain.Setup!.Id == capture.Context.SetupId &&
                domain.Bike!.Id == capture.Context.BikeId &&
                domain.Source!.SourceKind == RecordedSessionSourceKind.LiveCapture && domain.Source.SessionId == session.Id &&
                domain.Source.SourceHash == RecordedSessionSourceHash.Compute(
                    domain.Source.SourceKind, domain.Source.SourceName, domain.Source.SchemaVersion, domain.Payload.ToArray())), Arg.Any<CancellationToken>());
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
            Arg.Is<IReadOnlyCollection<RecordedSessionSourceSnapshot>>(ids => ids.Count == 1 && ids.Select(snapshot => snapshot.SessionId).Contains(session.Id)),
            Arg.Any<CancellationToken>());
        await sessionStore.Received(1).PublishSessionsChangedAsync(
            Arg.Is<IReadOnlyCollection<SessionSnapshot>>(ids => ids.Count == 1 && ids.Select(snapshot => snapshot.Id).Contains(session.Id)),
            Arg.Any<CancellationToken>());

        var saved = Assert.IsType<LiveSessionSaveResult.Saved>(result);
        Assert.Equal(session.Id, saved.SessionId);
        Assert.Equal(9, saved.Updated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveLiveCaptureAsync_ReturnsSavedWithWarning_WhenPostCommitPublicationFails(
        bool sourcePublicationFails)
    {
        var capture = CreateLiveCapturePackage(withGps: false);
        var session = new Session(
            Guid.NewGuid(),
            "live session",
            "desc",
            capture.Context.SetupId,
            capture.TelemetryCapture.Metadata.Timestamp);
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
            .Returns(fresh);
        if (sourcePublicationFails)
        {
            sourceStore.PublishSourcesChangedAsync(
                    Arg.Any<IReadOnlyCollection<RecordedSessionSourceSnapshot>>(),
                    Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("source publish failed"));
        }
        else
        {
            sessionStore.PublishSessionsChangedAsync(
                    Arg.Any<IReadOnlyCollection<SessionSnapshot>>(),
                    Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("session publish failed"));
        }

        var result = await CreateCoordinator().SaveLiveCaptureAsync(
            session,
            capture,
            SessionPreferences.Default, cancellationToken: TestContext.Current.CancellationToken);

        var saved = Assert.IsType<LiveSessionSaveResult.Saved>(result);
        Assert.Equal(session.Id, saved.SessionId);
        Assert.NotNull(saved.PublicationWarning);
        Assert.Contains("saved", saved.PublicationWarning, StringComparison.OrdinalIgnoreCase);
        await sessionTelemetryWriter.Received(1).PutProcessedSessionAsync(
            Arg.Is<Session>(savedSession => savedSession.Id == session.Id),
            Arg.Any<ProcessedTelemetryPayload>(),
            Arg.Any<Track?>(),
            Arg.Any<RecordedSessionSource?>());
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

        var result = await CreateCoordinator().SaveLiveCaptureAsync(session, capture, SessionPreferences.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<LiveSessionSaveResult.Failed>(result);
        await sessionStore.DidNotReceive().PublishSessionsChangedAsync(
            Arg.Any<IReadOnlyCollection<SessionSnapshot>>(),
            Arg.Any<CancellationToken>());
        await sourceStore.DidNotReceive().PublishSourcesChangedAsync(
            Arg.Any<IReadOnlyCollection<RecordedSessionSourceSnapshot>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_WhenTransactionFails_KeepsSessionAndPreferences()
    {
        var original = TestSnapshots.Session();
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        repository.Seed(original);
        using var store = new SessionStore(new StorePublication(new InlineUiThreadDispatcher()));
        await store.ReplaceAllAsync([original]);
        sessionPersistenceTransactions.DeleteSessionAsync(original.Id, null, false, Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("database diagnostic"));

        var result = await CreateCoordinator(store, repository.Repository).DeleteAsync(original.Id);

        Assert.Equal(SessionDeleteFailureReason.Delete, Assert.IsType<SessionDeleteResult.Failed>(result).Reason);
        Assert.Equal(original, store.Get(original.Id));
        Assert.Equal(original, SessionSnapshot.From((await repository.Repository.GetSessionAsync(original.Id))!));
        await sessionPreferences.DidNotReceive().RemoveRecordedAsync(original.Id);
    }

    [Fact]
    public async Task DeleteAsync_DeletesOrphanedTrackAndPublishesRemovals()
    {
        var id = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        sessionRepository.GetSessionAsync(id).Returns(new Session(id, "name", "desc", null) { FullTrack = trackId });
        sessionRepository.HasOtherActiveSessionWithFullTrackAsync(trackId, id).Returns(false);

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.IsType<SessionDeleteResult.Deleted>(result);
        await sessionPersistenceTransactions.Received(1).DeleteSessionAsync(
            id,
            trackId,
            deleteFullTrack: true,
            Arg.Any<CancellationToken>());
        await sourceStore.Received(1).PublishSourcesRemovedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(id)),
            Arg.Any<CancellationToken>());
        await sessionPreferences.Received(1).RemoveRecordedAsync(id);
        await sessionStore.Received(1).PublishSessionsRemovedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(id)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_WhenSourceIsKept_DoesNotRemoveSource()
    {
        var id = Guid.NewGuid();
        sessionRepository.GetSessionAsync(id).Returns(new Session(id, "name", "desc", null));
        sessionPersistenceTransactions.DeleteSessionAsync(id, null, false, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.IsType<SessionDeleteResult.Deleted>(result);
        await sourceStore.DidNotReceive().PublishSourcesRemovedAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadDetailAsync_ReturnsTelemetryBeforeTrackLoad()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();

        sessionStore.Get(snapshot.Id).Returns(snapshot);
        SetLocalTelemetry(snapshot.Id, telemetry);
        var progress = new RecordingProgress<SessionDetailLoadProgress>();

        var result = await CreateCoordinator().LoadDetailAsync(snapshot.Id, progress, cancellationToken: TestContext.Current.CancellationToken);

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
        var progress = new RecordingProgress<SessionDetailLoadProgress>();

        var result = await CreateCoordinator().LoadTrackAsync(snapshot.Id, telemetry, progress, cancellationToken: TestContext.Current.CancellationToken);

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
        var progress = new RecordingProgress<SessionDetailLoadProgress>();

        var result = await CreateCoordinator().LoadTrackAsync(snapshot.Id, telemetry, progress, cancellationToken: TestContext.Current.CancellationToken);

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
        var progress = new RecordingProgress<SessionDetailLoadProgress>();

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
        var progress = new RecordingProgress<SessionDetailLoadProgress>();

        var result = await CreateCoordinator().LoadDetailAsync(
            snapshot.Id,
            progress, cancellationToken: TestContext.Current.CancellationToken);

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

    private SessionLoader CreateLoader() =>
        new(
            sessionStore,
            processedTelemetryReader,
            trackCoordinator,
            domainQuery);

    private SessionCoordinator CreateCoordinator(ISessionStoreWriter? store = null, ISessionRepository? repository = null) =>
        new(
            CreateLoader(),
            CreateCommandService(store, repository),
            store ?? sessionStore,
            repository ?? sessionRepository,
            recomputeEngine,
            backgroundTaskRunner,
            new InlineUiThreadDispatcher());

    private SessionCommandService CreateCommandService(ISessionStoreWriter? store = null, ISessionRepository? repository = null) =>
        new(
            store ?? sessionStore,
            repository ?? sessionRepository,
            sessionTelemetryWriter,
            setupRepository,
            bikeRepository,
            sourceStore,
            reprocessor,
            backgroundTaskRunner,
            sessionPreferences,
            recomputeEngine,
            sessionPersistenceTransactions);

    private void SetLocalTelemetry(Guid sessionId, TelemetryData? telemetry)
    {
        processedTelemetryReader.Set(sessionId, telemetry);
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
                return Task.FromResult<RecordedSessionReprocessResult>(new RecordedSessionReprocessResult.Processed(
                    new ProcessedTelemetryPayload(
                        telemetryData,
                        telemetryData.BinaryForm,
                        AppJson.Serialize(fingerprint)),
                    fullTrack,
                    fingerprint));
            });
    }

}
