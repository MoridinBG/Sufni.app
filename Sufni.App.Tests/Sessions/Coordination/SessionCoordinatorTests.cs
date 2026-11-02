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
    private readonly ISessionPersistenceTransactionRunner sessionPersistenceTransactions = Substitute.For<ISessionPersistenceTransactionRunner>();
    private readonly ISessionPreferences sessionPreferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
    private readonly IRecordedSessionSourceStoreWriter sourceStore = Substitute.For<IRecordedSessionSourceStoreWriter>();
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

    [Fact]
    public async Task PreferenceReadFailure_ReturnsTypedReasonAndDiagnostics()
    {
        var id = Guid.NewGuid();
        sessionPreferences.GetRecordedAsync(id).ThrowsAsync(new IOException("file inaccessible"));

        var result = Assert.IsType<SessionPreferenceLoadResult.Failed>(await CreateCoordinator().LoadPreferencesAsync(id));

        Assert.Equal(SessionEditorFailureReason.LoadPreferences, result.Reason);
        Assert.Equal("file inaccessible", result.Details);
    }

    [Fact]
    public async Task PreferenceWriteFailure_ReturnsTypedReasonAndDiagnostics()
    {
        var id = Guid.NewGuid();
        sessionPreferences.UpdateRecordedAsync(id, Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .ThrowsAsync(new IOException("disk full"));

        var result = Assert.IsType<SessionPreferenceSaveResult.Failed>(await CreateCoordinator().SavePreferencesAsync(id,
            current => current with { SignalDisplay = current.SignalDisplay with { VelocitySmoothing = PlotSmoothingLevel.Strong } }));

        Assert.Equal(SessionEditorFailureReason.SavePreferences, result.Reason);
        Assert.Equal("disk full", result.Details);
    }

    [Fact]
    public async Task ReconcileStaleness_DistinguishesCurrentActiveAndRequiredWithoutStartingWork()
    {
        var session = TestSnapshots.Session();
        var coordinator = CreateCoordinator();
        var current = new RecordedSessionDomainSnapshot(session, null, null, null, null, null, new SessionStaleness.Current());
        Assert.IsType<SessionStalenessReconciliation.Current>(await coordinator.ReconcileStalenessAsync(current));

        var stale = current with { Staleness = new SessionStaleness.Stale() };
        var required = Assert.IsType<SessionStalenessReconciliation.Required>(await coordinator.ReconcileStalenessAsync(stale));
        Assert.Equal(session.ProcessingFingerprintJson, required.StoredFingerprint);
        recomputeEngine.IsActive(session.Id).Returns(true);
        Assert.IsType<SessionStalenessReconciliation.Active>(await coordinator.ReconcileStalenessAsync(stale, SessionRecomputeChoice.ThisSession));
        await recomputeEngine.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReconcileStaleness_ReportsUnavailableInputsWithoutStartingWork(bool missingSource)
    {
        SessionStaleness reason = missingSource ? new SessionStaleness.MissingRawSource()
            : new SessionStaleness.MissingDependencies(true, false);
        var domain = new RecordedSessionDomainSnapshot(TestSnapshots.Session(), null, null, null, null, null, reason);

        var result = await CreateCoordinator().ReconcileStalenessAsync(domain, SessionRecomputeChoice.ThisSession);

        Assert.Equal(reason, Assert.IsType<SessionStalenessReconciliation.Unavailable>(result).Reason);
        await recomputeEngine.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileStaleness_ConfirmedSessionReturnsTheRecomputeOutcome()
    {
        var domain = new RecordedSessionDomainSnapshot(TestSnapshots.Session(), null, null, null, null, null, new SessionStaleness.Stale());
        var outcome = new SessionRecomputeResult.ProcessingFailed(SessionProcessingFailureReason.InvalidCalibration, "calibration details");
        recomputeEngine.RequestRecomputeAsync(domain.Session.Id).Returns(outcome);

        var result = await CreateCoordinator().ReconcileStalenessAsync(domain, SessionRecomputeChoice.ThisSession);

        Assert.Equal(outcome, Assert.IsType<SessionStalenessReconciliation.Recomputed>(result).Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReconcileStaleness_BulkPublishesItsOutcomeForShellFeedback(bool fails)
    {
        var coordinator = CreateCoordinator();
        var outcomes = new List<SessionRecomputeAllOutcome>();
        coordinator.RecomputeAllFinished += (_, outcome) => outcomes.Add(outcome);
        var domain = new RecordedSessionDomainSnapshot(TestSnapshots.Session(), null, null, null, null, null, new SessionStaleness.Stale());
        var summary = new SessionRecomputeAllResult(4, 2, 0, 1, 1);
        if (fails) recomputeEngine.RequestRecomputeAllAsync().ThrowsAsync(new IOException("database inaccessible"));
        else recomputeEngine.RequestRecomputeAllAsync().Returns(summary);

        var result = Assert.IsType<SessionStalenessReconciliation.Bulk>(
            await coordinator.ReconcileStalenessAsync(domain, SessionRecomputeChoice.AllSessions));

        Assert.Equal(result.Outcome, Assert.Single(outcomes));
        if (fails)
        {
            var failure = Assert.IsType<SessionRecomputeAllOutcome.Failed>(result.Outcome);
            Assert.Equal(SessionRecomputeFailureReason.RecomputeAll, failure.Reason);
            Assert.Equal("database inaccessible", failure.Details);
        }
        else Assert.Equal(summary, Assert.IsType<SessionRecomputeAllOutcome.Completed>(result.Outcome).Result);
        await recomputeEngine.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(65.0)]
    public async Task ShiftGpsOffsetAsync_PersistsOffsetWithoutWritingGpsMetrics(double? storedDuration)
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
        Assert.Equal(storedDuration, saved.DurationSeconds);
        Assert.Null(saved.DistanceMeters);
        Assert.Equal(20, saved.Updated);
        Assert.Equal(SessionSnapshot.From(saved), store.Get(original.Id));
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
        trackStore.GetAll().Returns([new TrackSnapshot(track.Id, track.StartTime, track.EndTime, 1)]);
        var snapshot = TestSnapshots.Session(hasProcessedData: true, updated: 5, timestamp: telemetry.Metadata.Timestamp)
            with { GpsOffsetSeconds = 1.25, DurationSeconds = storedDuration };
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
        var draftBase = SessionDraft.From(existing);
        var draft = draftBase with { Name = "renamed" };

        var result = await CreateCoordinator(store, repository.Repository).SaveAsync(draft, draftBase);

        var persisted = (await repository.Repository.GetSessionAsync(existing.Id))!;
        if (fail)
        {
            Assert.Equal(SessionSaveFailureReason.Save, Assert.IsType<SessionSaveResult.Failed>(result).Reason);
            Assert.Equal(existing, store.Get(existing.Id));
            Assert.Equal(existing, SessionSnapshot.From(persisted));
        }
        else
        {
            Assert.Equal(7, Assert.IsType<SessionSaveResult.Saved>(result).Snapshot.Updated);
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
    public async Task SaveAsync_AcceptsUnchangedDraftFieldsAfterAlignmentProcessingAndDerivedChanges()
    {
        var original = TestSnapshots.Session(updated: 5, timestamp: 100);
        var current = original with
        {
            Updated = 10, GpsOffsetSeconds = 2.5, VelocityFilterWindowMilliseconds = 250,
            Timestamp = 200, DurationSeconds = 120, HasProcessedData = true,
        };
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        repository.Seed(current);
        using var store = new SessionStore(publication);
        await store.ReplaceAllAsync([current]);
        var draftBase = SessionDraft.From(original);

        var result = await CreateCoordinator(store, repository.Repository).SaveAsync(draftBase with { Name = "local edit" }, draftBase);

        var saved = Assert.IsType<SessionSaveResult.Saved>(result).Snapshot;
        Assert.Equal("local edit", saved.Name);
        Assert.Equal(2.5, saved.GpsOffsetSeconds);
        Assert.Equal(250, saved.VelocityFilterWindowMilliseconds);
        Assert.Equal(200, saved.Timestamp);
        Assert.Equal(120, saved.DurationSeconds);
        Assert.True(saved.HasProcessedData);
        Assert.Equal(saved, store.Get(original.Id));
    }

    [Fact]
    public async Task SaveAsync_ReturnsConflictWithoutOverwritingChangedDraftFields()
    {
        var current = TestSnapshots.Session(updated: 10);
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        repository.Seed(current);
        using var store = new SessionStore(publication);
        await store.ReplaceAllAsync([current]);

        var draftBase = SessionDraft.From(current with { Name = "before remote edit" });
        var result = await CreateCoordinator(store, repository.Repository).SaveAsync(draftBase with { Name = "local edit" }, draftBase);

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

        var result = await CreateCoordinator(store).SaveAsync(SessionDraft.From(existing), SessionDraft.From(existing));

        Assert.IsType<SessionSaveResult.Missing>(result);
        Assert.Null(store.Get(existing.Id));
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
            sources ?? sourceStore,
            backgroundTaskRunner,
            sessionPreferences,
            recomputeEngine,
            sessionPersistenceTransactions,
            new InlineUiThreadDispatcher(),
            processedTelemetryReader,
            trackRepository,
            publication,
            tracks ?? trackStore);

}
