using Sufni.App.Tests.TestSupport.Async;
using System.Threading;
using NSubstitute;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Stores;
using Microsoft.Extensions.Time.Testing;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Sessions.Coordination;

// Engine-level coverage for the serialized cancel-and-replace recompute engine and
// its role as the recompute-liveness owner.
public class SessionRecomputeEngineTests
{
    private readonly ISessionStoreWriter sessionStore = Substitute.For<ISessionStoreWriter>();
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ISessionTelemetryWriter sessionTelemetryWriter = Substitute.For<ISessionTelemetryWriter>();
    private readonly ISynchronizableRepository<Track> trackEntityRepository = Substitute.For<ISynchronizableRepository<Track>>();
    private readonly IRecordedSessionSourceRepository sourceRepository = Substitute.For<IRecordedSessionSourceRepository>();
    private readonly IRecordedSessionSourceStoreWriter sourceStore = Substitute.For<IRecordedSessionSourceStoreWriter>();
    private readonly IRecordedSessionDomainQuery domainQuery = Substitute.For<IRecordedSessionDomainQuery>();
    private readonly IRecordedSessionReprocessor reprocessor = Substitute.For<IRecordedSessionReprocessor>();

    private SessionRecomputeEngine CreateEngine(ISessionStoreWriter? store = null, ISessionRepository? repository = null) => new(
        store ?? sessionStore,
        repository ?? sessionRepository,
        sessionTelemetryWriter,
        trackEntityRepository,
        new InlineBackgroundTaskRunner(),
        sourceStore,
        domainQuery,
        reprocessor,
        sourceRepository);


    // Wires a recomputable session: a current-schema domain, a present source whose
    // snapshot matches the domain (so the refresh branch is skipped), a persisted row,
    // and a derived write that echoes the row back as the fresh result by default.
    private Session ConfigureRecomputable(Guid sessionId)
    {
        var setupId = Guid.NewGuid();
        var bikeId = Guid.NewGuid();
        var session = TestSnapshots.Session(id: sessionId, setupId: setupId, hasProcessedData: true);
        var setup = TestSnapshots.Setup(id: setupId, bikeId: bikeId);
        var bike = TestSnapshots.Bike(id: bikeId);
        var source = PersistenceTestData.CreateRecordedSessionSource(sessionId);
        var domain = new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            null,
            null,
            RecordedSessionSourceSnapshot.From(source),
            new SessionStaleness.Stale(),
            DerivedChangeKind.None);

        domainQuery.Get(sessionId).Returns(domain);
        sourceRepository.GetRecordedSessionSourceAsync(sessionId).Returns(source);
        var persisted = new Session(sessionId, "session", "desc", setupId, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(60)
        };
        sessionRepository.GetSessionAsync(sessionId).Returns(persisted);
        sessionTelemetryWriter
            .UpdateProcessedDerivedDataAsync(
                Arg.Any<Session>(),
                Arg.Any<ProcessedTelemetryPayload>(),
                Arg.Any<Track?>(),
                Arg.Any<ProcessingFingerprint>())
            .Returns(callInfo => callInfo.Arg<Session>());
        return persisted;
    }

    private static RecordedSessionReprocessResult ReprocessResult(TelemetryProcessingOptions options)
    {
        var telemetryData = TestTelemetryData.CreateProcessed();
        var fingerprint = new ProcessingFingerprint(
            3,
            3,
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "dep",
            "src",
            options.ClampedVelocityFilterWindowMilliseconds);
        return new RecordedSessionReprocessResult.Processed(
            new ProcessedTelemetryPayload(telemetryData, telemetryData.BinaryForm, AppJson.Serialize(fingerprint)),
            GeneratedFullTrack: null,
            fingerprint);
    }

    [Fact]
    public async Task RecomputeAllAsync_SkipsSessionThatDisappeared_AfterEnumeration()
    {
        var id = Guid.NewGuid();
        sessionRepository.GetActiveSessionIdsAsync().Returns([id]);
        using var engine = CreateEngine();

        var result = await engine.RequestRecomputeAllAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.NotRecomputable);
        Assert.Equal(0, result.Failed);
        Assert.False(engine.IsActive(id));
    }

    [Fact]
    public async Task RequestRecomputeAsync_ProcessingFailure_PreservesPublishedAndPersistedSession()
    {
        var id = Guid.NewGuid();
        var existing = SessionSnapshot.From(ConfigureRecomputable(id));
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        repository.Seed(existing);
        using var store = new SessionStore(new StorePublication(new InlineUiThreadDispatcher()));
        await store.ReplaceAllAsync([existing]);
        reprocessor.ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>())
            .Returns(new RecordedSessionReprocessResult.Failed(SessionProcessingFailureReason.InvalidCalibration, "calibration diagnostic"));
        using var engine = CreateEngine(store, repository.Repository);

        var result = await engine.RequestRecomputeAsync(id);

        Assert.Equal(SessionProcessingFailureReason.InvalidCalibration, Assert.IsType<SessionRecomputeResult.ProcessingFailed>(result).Reason);
        Assert.Equal(existing, store.Get(id));
        Assert.Equal(existing, SessionSnapshot.From((await repository.Repository.GetSessionAsync(id))!));
        Assert.False(engine.IsActive(id));
        await sessionTelemetryWriter.DidNotReceive().UpdateProcessedDerivedDataAsync(
            Arg.Any<Session>(), Arg.Any<ProcessedTelemetryPayload>(), Arg.Any<Track?>(), Arg.Any<ProcessingFingerprint>());
    }

    [Fact]
    public async Task Stop_WaitsForDisplacedProcessingWithoutLettingItCommit()
    {
        var id = Guid.NewGuid();
        ConfigureRecomputable(id);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<RecordedSessionReprocessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        reprocessor.ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (++calls != 1) return Task.FromResult(ReprocessResult(TelemetryProcessingOptions.Default));
            entered.SetResult();
            return release.Task;
        });
        using var engine = CreateEngine();
        var displaced = engine.RequestRecomputeAsync(id);
        await entered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.IsType<SessionRecomputeResult.Recomputed>(await engine.RequestRecomputeAsync(id));

        var stop = engine.StopAsync();
        Assert.False(stop.IsCompleted);
        release.SetResult(ReprocessResult(TelemetryProcessingOptions.Default));
        await stop.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<SessionRecomputeResult.Superseded>(await displaced);
        await sessionTelemetryWriter.Received(1).UpdateProcessedDerivedDataAsync(Arg.Any<Session>(),
            Arg.Any<ProcessedTelemetryPayload>(), Arg.Any<Track?>(), Arg.Any<ProcessingFingerprint>());
    }

    [Fact]
    public async Task RequestRecomputeAsync_CancelAndReplace_SupersedesFirst_CommitsOnce_WithLaterOption()
    {
        var sessionId = Guid.NewGuid();
        var persisted = ConfigureRecomputable(sessionId);
        persisted.VelocityFilterWindowMilliseconds = 100;
        var domain = domainQuery.Get(sessionId)!;
        domainQuery.Get(sessionId).Returns(domain with
        {
            Session = domain.Session with { VelocityFilterWindowMilliseconds = 100 }
        });

        ProcessingFingerprint? committedFingerprint = null;
        sessionTelemetryWriter
            .UpdateProcessedDerivedDataAsync(
                Arg.Any<Session>(),
                Arg.Any<ProcessedTelemetryPayload>(),
                Arg.Any<Track?>(),
                Arg.Any<ProcessingFingerprint>())
            .Returns(callInfo =>
            {
                committedFingerprint = callInfo.Arg<ProcessingFingerprint>();
                return callInfo.Arg<Session>();
            });

        var firstReprocessStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReprocessStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReprocessGate = new TaskCompletionSource<RecordedSessionReprocessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reprocessCalls = 0;
        reprocessor
            .ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var options = callInfo.ArgAt<RecordedSessionDerivationInput>(0).ProcessingOptions;
                var callNumber = Interlocked.Increment(ref reprocessCalls);
                if (callNumber == 1)
                {
                    firstReprocessStarted.TrySetResult();
                    return firstReprocessGate.Task;
                }

                secondReprocessStarted.TrySetResult();
                return Task.FromResult(ReprocessResult(options));
            });

        var engine = CreateEngine();

        var firstTask = engine.RequestRecomputeAsync(sessionId);
        // Liveness is owned synchronously: the request is active before any await.
        Assert.True(engine.IsActive(sessionId));
        await firstReprocessStarted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        // A second request for the same id cancels-and-replaces the first.
        persisted.VelocityFilterWindowMilliseconds = 250;
        domainQuery.Get(sessionId).Returns(domain with
        {
            Session = domain.Session with { VelocityFilterWindowMilliseconds = 250 }
        });
        var secondTask = engine.RequestRecomputeAsync(sessionId);

        // The replacement starts and commits before the old uninterruptible
        // reprocess physically finishes.
        await secondReprocessStarted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        var secondResult = await secondTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<SessionRecomputeResult.Recomputed>(secondResult);

        // Release the (now superseded) first reprocess so it reaches its still-current check.
        firstReprocessGate.SetResult(ReprocessResult(new TelemetryProcessingOptions(100)));
        var firstResult = await firstTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<SessionRecomputeResult.Superseded>(firstResult);
        // Exactly one committer, and it committed the LATER option's fingerprint.
        await sessionTelemetryWriter.Received(1).UpdateProcessedDerivedDataAsync(
            Arg.Any<Session>(),
            Arg.Any<ProcessedTelemetryPayload>(),
            Arg.Any<Track?>(),
            Arg.Any<ProcessingFingerprint>());
        Assert.NotNull(committedFingerprint);
        Assert.Equal(250, committedFingerprint!.VelocityFilterWindowMilliseconds);
        Assert.False(engine.IsActive(sessionId));
    }

    [Fact]
    public async Task RequestRecomputeAsync_ConcurrentDifferentSessions_BothComplete()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        ConfigureRecomputable(firstId);
        ConfigureRecomputable(secondId);
        reprocessor
            .ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(ReprocessResult(callInfo.ArgAt<RecordedSessionDerivationInput>(0).ProcessingOptions)));

        var engine = CreateEngine();
        var firstTask = engine.RequestRecomputeAsync(firstId);
        var secondTask = engine.RequestRecomputeAsync(secondId);

        Assert.IsType<SessionRecomputeResult.Recomputed>(await firstTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2)));
        Assert.IsType<SessionRecomputeResult.Recomputed>(await secondTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2)));
        Assert.False(engine.IsActive(firstId));
        Assert.False(engine.IsActive(secondId));
    }

    [Fact]
    public async Task RequestRecomputeAsync_ReEnqueues_WhenDerivedWriteRollsBackOnPassiveInputChange()
    {
        var sessionId = Guid.NewGuid();
        var persisted = ConfigureRecomputable(sessionId);
        reprocessor
            .ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(ReprocessResult(callInfo.ArgAt<RecordedSessionDerivationInput>(0).ProcessingOptions)));
        // The in-transaction DB-input re-check rolls back once (passive dependency
        // change), then succeeds: the engine self-heals by looping, not by surfacing a
        // neutral result.
        sessionTelemetryWriter
            .UpdateProcessedDerivedDataAsync(
                Arg.Any<Session>(),
                Arg.Any<ProcessedTelemetryPayload>(),
                Arg.Any<Track?>(),
                Arg.Any<ProcessingFingerprint>())
            .Returns((Session?)null, persisted);

        var result = await CreateEngine()
            .RequestRecomputeAsync(sessionId)
            .AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<SessionRecomputeResult.Recomputed>(result);
        await sessionTelemetryWriter.Received(2).UpdateProcessedDerivedDataAsync(
            Arg.Any<Session>(),
            Arg.Any<ProcessedTelemetryPayload>(),
            Arg.Any<Track?>(),
            Arg.Any<ProcessingFingerprint>());
        await reprocessor.Received(2).ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestRecomputeAsync_LoadsSourceFromDerivationWindow()
    {
        var sessionId = Guid.NewGuid();
        var sourceSessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var bikeId = Guid.NewGuid();
        var session = TestSnapshots.Session(id: sessionId, setupId: setupId, hasProcessedData: true);
        var setup = TestSnapshots.Setup(id: setupId, bikeId: bikeId);
        var bike = TestSnapshots.Bike(id: bikeId);
        var source = PersistenceTestData.CreateRecordedSessionSource(sourceSessionId);
        var window = new RecordedSessionDerivationWindow(sourceSessionId, 1, 2);
        session = session with
        {
            SourceSessionId = sourceSessionId,
            WindowStartSeconds = 1,
            WindowEndSeconds = 2
        };
        var domain = new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            null,
            null,
            RecordedSessionSourceSnapshot.From(source),
            new SessionStaleness.Stale(),
            DerivedChangeKind.None);
        domainQuery.Get(sessionId).Returns(domain);
        sourceRepository.GetRecordedSessionSourceAsync(sourceSessionId).Returns(source);
        var persisted = new Session(sessionId, "derived", "desc", setupId, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(60)
        };
        sessionRepository.GetSessionAsync(sessionId).Returns(persisted);
        reprocessor
            .ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(ReprocessResult(callInfo.ArgAt<RecordedSessionDerivationInput>(0).ProcessingOptions)));
        sessionTelemetryWriter
            .UpdateProcessedDerivedDataAsync(
                Arg.Any<Session>(),
                Arg.Any<ProcessedTelemetryPayload>(),
                Arg.Any<Track?>(),
                Arg.Any<ProcessingFingerprint>())
            .Returns(callInfo => callInfo.Arg<Session>());

        var result = await CreateEngine()
            .RequestRecomputeAsync(sessionId)
            .AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<SessionRecomputeResult.Recomputed>(result);
        await sourceRepository.Received(1).GetRecordedSessionSourceAsync(sourceSessionId);
        await sourceRepository.DidNotReceive().GetRecordedSessionSourceAsync(sessionId);
        await reprocessor.Received(1).ReprocessAsync(Arg.Is<RecordedSessionDerivationInput>(value => value.Session.DerivationWindow == window && value.Source.SessionId == sourceSessionId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestRecomputeAsync_ReturnsNotRecomputable_WhenStalenessCannotRecompute()
    {
        var sessionId = Guid.NewGuid();
        var session = TestSnapshots.Session(id: sessionId, hasProcessedData: true);
        domainQuery.Get(sessionId).Returns(new RecordedSessionDomainSnapshot(
            session,
            null,
            null,
            null,
            null,
            null,
            new SessionStaleness.MissingDependencies(SetupMissing: true, BikeMissing: false),
            DerivedChangeKind.None));

        var engine = CreateEngine();
        var result = await engine
            .RequestRecomputeAsync(sessionId)
            .AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<SessionRecomputeResult.NotRecomputable>(result);
        await sessionTelemetryWriter.DidNotReceive().UpdateProcessedDerivedDataAsync(
            Arg.Any<Session>(),
            Arg.Any<ProcessedTelemetryPayload>(),
            Arg.Any<Track?>(),
            Arg.Any<ProcessingFingerprint>());
        Assert.False(engine.IsActive(sessionId));
    }

    [Fact]
    public async Task RequestRecomputeAllAsync_ExcludesCurrentSessions_AndSkipsUnavailableOrMissingSessions()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var skippedId = Guid.NewGuid();
        var firstPersisted = ConfigureRecomputable(firstId);
        var secondPersisted = ConfigureRecomputable(secondId);
        var currentId = Guid.NewGuid();
        ConfigureRecomputable(currentId);
        var currentDomain = domainQuery.Get(currentId)! with { Staleness = new SessionStaleness.Current() };
        domainQuery.Get(currentId).Returns(currentDomain);
        var missingId = Guid.NewGuid();

        // A session whose current staleness cannot be recomputed is enumerated but
        // skipped by the engine's guard, not surfaced as a failure.
        var skipped = TestSnapshots.Session(id: skippedId, hasProcessedData: true);
        domainQuery.Get(skippedId).Returns(new RecordedSessionDomainSnapshot(
            skipped,
            null,
            null,
            null,
            null,
            null,
            new SessionStaleness.MissingDependencies(SetupMissing: true, BikeMissing: false),
            DerivedChangeKind.None));
        var skippedPersisted = new Session(skippedId, "skipped", "desc", Guid.NewGuid(), 100);

        sessionRepository.GetActiveSessionIdsAsync().Returns([firstPersisted.Id, secondPersisted.Id, skippedPersisted.Id, currentId, missingId]);
        reprocessor
            .ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(ReprocessResult(callInfo.ArgAt<RecordedSessionDerivationInput>(0).ProcessingOptions)));

        var engine = CreateEngine();
        var summary = await engine
            .RequestRecomputeAllAsync()
            .AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(4, summary.Total);
        Assert.Equal(2, summary.Recomputed);
        Assert.Equal(2, summary.NotRecomputable);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.Superseded);
        await sessionTelemetryWriter.Received(2).UpdateProcessedDerivedDataAsync(
            Arg.Any<Session>(),
            Arg.Any<ProcessedTelemetryPayload>(),
            Arg.Any<Track?>(),
            Arg.Any<ProcessingFingerprint>());
        Assert.False(engine.IsActive(firstId));
        Assert.False(engine.IsActive(secondId));
    }

    [Fact]
    public async Task RequestRecomputeAllAsync_UsesActiveSessionIdProjection()
    {
        var liveId = Guid.NewGuid();
        ConfigureRecomputable(liveId);
        var deletedId = Guid.NewGuid();
        sessionRepository.GetActiveSessionIdsAsync().Returns([liveId]);
        reprocessor
            .ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(ReprocessResult(callInfo.ArgAt<RecordedSessionDerivationInput>(0).ProcessingOptions)));

        var summary = await CreateEngine()
            .RequestRecomputeAllAsync()
            .AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, summary.Total);
        Assert.Equal(1, summary.Recomputed);
        await sessionRepository.Received(1).GetActiveSessionIdsAsync();
        domainQuery.DidNotReceive().Get(deletedId);
    }

    [Fact]
    public async Task RequestRecomputeAllAsync_ReportsProgress_FromZeroToTotal()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        ConfigureRecomputable(firstId);
        ConfigureRecomputable(secondId);
        sessionRepository.GetActiveSessionIdsAsync().Returns([firstId, secondId]);
        reprocessor
            .ReprocessAsync(Arg.Any<RecordedSessionDerivationInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(ReprocessResult(callInfo.ArgAt<RecordedSessionDerivationInput>(0).ProcessingOptions)));

        // A synchronous sink records every report deterministically: the engine
        // reports before each parallel body returns, and ForEachAsync awaits them all.
        var collector = new RecordingProgress<SessionRecomputeAllProgress>();

        var summary = await CreateEngine()
            .RequestRecomputeAllAsync(collector)
            .AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, summary.Total);
        Assert.Contains(new SessionRecomputeAllProgress(0, 2), collector.Reports);
        Assert.Contains(collector.Reports, report => report is { Completed: 2, Total: 2 });
        Assert.All(collector.Reports, report => Assert.Equal(2, report.Total));
    }

}
