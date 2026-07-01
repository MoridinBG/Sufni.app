using System.Threading;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.SessionGraph;
using Sufni.App.ExtensionHost.TestSupport;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.SessionGraph;
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
    private readonly ISynchronizableRepository<Session> sessionEntityRepository = Substitute.For<ISynchronizableRepository<Session>>();
    private readonly ISessionPreferences sessionPreferences = Substitute.For<ISessionPreferences>();
    private readonly IRecordedSessionSourceStoreWriter sourceStore = Substitute.For<IRecordedSessionSourceStoreWriter>();
    private readonly IRecordedSessionDomainQuery domainQuery = Substitute.For<IRecordedSessionDomainQuery>();
    private readonly IRecordedSessionReprocessor reprocessor = Substitute.For<IRecordedSessionReprocessor>();

    private SessionRecomputeEngine CreateEngine() => new(
        sessionStore,
        sessionRepository,
        sessionTelemetryWriter,
        trackEntityRepository,
        sessionEntityRepository,
        new InlineBackgroundTaskRunner(),
        sessionPreferences,
        sourceStore,
        domainQuery,
        reprocessor);

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
            new SessionStaleness.DependencyHashChanged(),
            DerivedChangeKind.None);

        domainQuery.Get(sessionId).Returns(domain);
        sourceStore.LoadAsync(sessionId, Arg.Any<CancellationToken>()).Returns(source);
        var persisted = new Session(sessionId, "session", "desc", setupId, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(60)
        };
        sessionRepository.GetSessionAsync(sessionId).Returns(persisted);
        sessionEntityRepository.GetAllAsync().Returns([persisted]);
        sessionTelemetryWriter
            .UpdateProcessedDerivedDataAsync(Arg.Any<Session>(), Arg.Any<Track?>(), Arg.Any<ProcessingFingerprint>())
            .Returns(callInfo => callInfo.Arg<Session>());
        return persisted;
    }

    private static RecordedSessionReprocessResult ReprocessResult(TelemetryProcessingOptions options) => new(
        TestTelemetryData.CreateProcessed(),
        GeneratedFullTrack: null,
        new ProcessingFingerprint(3, 3, Guid.NewGuid(), Guid.NewGuid(), 1, "dep", "src", options.ClampedVelocityFilterWindowMilliseconds));

    [Fact]
    public async Task RequestRecomputeAsync_CancelAndReplace_SupersedesFirst_CommitsOnce_WithLaterOption()
    {
        var sessionId = Guid.NewGuid();
        ConfigureRecomputable(sessionId);
        sessionPreferences.GetRecordedAsync(sessionId).Returns(
            SessionPreferences.Default with { Processing = new SessionProcessingPreferences(100) },
            SessionPreferences.Default with { Processing = new SessionProcessingPreferences(250) });

        ProcessingFingerprint? committedFingerprint = null;
        sessionTelemetryWriter
            .UpdateProcessedDerivedDataAsync(Arg.Any<Session>(), Arg.Any<Track?>(), Arg.Any<ProcessingFingerprint>())
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
            .ReprocessAsync(Arg.Any<RecordedSessionDomainSnapshot>(), Arg.Any<RecordedSessionSource>(), Arg.Any<TelemetryProcessingOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var options = callInfo.ArgAt<TelemetryProcessingOptions>(2);
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

        var firstTask = engine.RequestRecomputeAsync(sessionId, RecomputeReason.ProcessingPreferenceChanged);
        // Liveness is owned synchronously: the request is active before any await.
        Assert.True(engine.IsActive(sessionId));
        await firstReprocessStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // A second request for the same id cancels-and-replaces the first.
        var secondTask = engine.RequestRecomputeAsync(sessionId, RecomputeReason.ProcessingPreferenceChanged);

        // The replacement starts and commits before the old uninterruptible
        // reprocess physically finishes.
        await secondReprocessStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondResult = await secondTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<SessionRecomputeResult.Recomputed>(secondResult);

        // Release the (now superseded) first reprocess so it reaches its still-current check.
        firstReprocessGate.SetResult(ReprocessResult(new SessionProcessingPreferences(100).ToTelemetryProcessingOptions()));
        var firstResult = await firstTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<SessionRecomputeResult.Superseded>(firstResult);
        // Exactly one committer, and it committed the LATER option's fingerprint.
        await sessionTelemetryWriter.Received(1).UpdateProcessedDerivedDataAsync(
            Arg.Any<Session>(), Arg.Any<Track?>(), Arg.Any<ProcessingFingerprint>());
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
        sessionPreferences.GetRecordedAsync(Arg.Any<Guid>()).Returns(SessionPreferences.Default);
        reprocessor
            .ReprocessAsync(Arg.Any<RecordedSessionDomainSnapshot>(), Arg.Any<RecordedSessionSource>(), Arg.Any<TelemetryProcessingOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(ReprocessResult(callInfo.ArgAt<TelemetryProcessingOptions>(2))));

        var engine = CreateEngine();
        var firstTask = engine.RequestRecomputeAsync(firstId, RecomputeReason.ManualFromList);
        var secondTask = engine.RequestRecomputeAsync(secondId, RecomputeReason.ManualFromList);

        Assert.IsType<SessionRecomputeResult.Recomputed>(await firstTask.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.IsType<SessionRecomputeResult.Recomputed>(await secondTask.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(engine.IsActive(firstId));
        Assert.False(engine.IsActive(secondId));
    }

    [Fact]
    public async Task RequestRecomputeAsync_ReEnqueues_WhenDerivedWriteRollsBackOnPassiveInputChange()
    {
        var sessionId = Guid.NewGuid();
        var persisted = ConfigureRecomputable(sessionId);
        sessionPreferences.GetRecordedAsync(sessionId).Returns(SessionPreferences.Default);
        reprocessor
            .ReprocessAsync(Arg.Any<RecordedSessionDomainSnapshot>(), Arg.Any<RecordedSessionSource>(), Arg.Any<TelemetryProcessingOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(ReprocessResult(callInfo.ArgAt<TelemetryProcessingOptions>(2))));
        // The in-transaction DB-input re-check rolls back once (passive dependency
        // change), then succeeds: the engine self-heals by looping, not by surfacing a
        // neutral result.
        sessionTelemetryWriter
            .UpdateProcessedDerivedDataAsync(Arg.Any<Session>(), Arg.Any<Track?>(), Arg.Any<ProcessingFingerprint>())
            .Returns((Session?)null, persisted);

        var result = await CreateEngine()
            .RequestRecomputeAsync(sessionId, RecomputeReason.DependencyChanged)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<SessionRecomputeResult.Recomputed>(result);
        await sessionTelemetryWriter.Received(2).UpdateProcessedDerivedDataAsync(
            Arg.Any<Session>(), Arg.Any<Track?>(), Arg.Any<ProcessingFingerprint>());
        await reprocessor.Received(2).ReprocessAsync(
            Arg.Any<RecordedSessionDomainSnapshot>(), Arg.Any<RecordedSessionSource>(), Arg.Any<TelemetryProcessingOptions>(), Arg.Any<CancellationToken>());
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
            .RequestRecomputeAsync(sessionId, RecomputeReason.ManualFromList)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<SessionRecomputeResult.NotRecomputable>(result);
        await sessionTelemetryWriter.DidNotReceive().UpdateProcessedDerivedDataAsync(
            Arg.Any<Session>(), Arg.Any<Track?>(), Arg.Any<ProcessingFingerprint>());
        Assert.False(engine.IsActive(sessionId));
    }

    [Fact]
    public async Task RequestRecomputeAllAsync_RecomputesEveryRecomputableSession_AndCountsSkips()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var skippedId = Guid.NewGuid();
        var firstPersisted = ConfigureRecomputable(firstId);
        var secondPersisted = ConfigureRecomputable(secondId);

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

        sessionEntityRepository.GetAllAsync().Returns([firstPersisted, secondPersisted, skippedPersisted]);
        sessionPreferences.GetRecordedAsync(Arg.Any<Guid>()).Returns(SessionPreferences.Default);
        reprocessor
            .ReprocessAsync(Arg.Any<RecordedSessionDomainSnapshot>(), Arg.Any<RecordedSessionSource>(), Arg.Any<TelemetryProcessingOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(ReprocessResult(callInfo.ArgAt<TelemetryProcessingOptions>(2))));

        var engine = CreateEngine();
        var summary = await engine
            .RequestRecomputeAllAsync(RecomputeReason.RecomputeAll)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, summary.Total);
        Assert.Equal(2, summary.Recomputed);
        Assert.Equal(1, summary.NotRecomputable);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.Superseded);
        await sessionTelemetryWriter.Received(2).UpdateProcessedDerivedDataAsync(
            Arg.Any<Session>(), Arg.Any<Track?>(), Arg.Any<ProcessingFingerprint>());
        Assert.False(engine.IsActive(firstId));
        Assert.False(engine.IsActive(secondId));
    }

    [Fact]
    public async Task RequestRecomputeAllAsync_ExcludesSoftDeletedSessions()
    {
        var liveId = Guid.NewGuid();
        var livePersisted = ConfigureRecomputable(liveId);
        var deletedPersisted = new Session(Guid.NewGuid(), "deleted", "desc", Guid.NewGuid(), 100)
        {
            Deleted = 123
        };
        sessionEntityRepository.GetAllAsync().Returns([livePersisted, deletedPersisted]);
        sessionPreferences.GetRecordedAsync(Arg.Any<Guid>()).Returns(SessionPreferences.Default);
        reprocessor
            .ReprocessAsync(Arg.Any<RecordedSessionDomainSnapshot>(), Arg.Any<RecordedSessionSource>(), Arg.Any<TelemetryProcessingOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(ReprocessResult(callInfo.ArgAt<TelemetryProcessingOptions>(2))));

        var summary = await CreateEngine()
            .RequestRecomputeAllAsync(RecomputeReason.RecomputeAll)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, summary.Total);
        Assert.Equal(1, summary.Recomputed);
        domainQuery.DidNotReceive().Get(deletedPersisted.Id);
    }

    [Fact]
    public async Task RequestRecomputeAllAsync_ReportsProgress_FromZeroToTotal()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstPersisted = ConfigureRecomputable(firstId);
        var secondPersisted = ConfigureRecomputable(secondId);
        sessionEntityRepository.GetAllAsync().Returns([firstPersisted, secondPersisted]);
        sessionPreferences.GetRecordedAsync(Arg.Any<Guid>()).Returns(SessionPreferences.Default);
        reprocessor
            .ReprocessAsync(Arg.Any<RecordedSessionDomainSnapshot>(), Arg.Any<RecordedSessionSource>(), Arg.Any<TelemetryProcessingOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(ReprocessResult(callInfo.ArgAt<TelemetryProcessingOptions>(2))));

        // A synchronous sink records every report deterministically: the engine
        // reports before each parallel body returns, and ForEachAsync awaits them all.
        var collector = new RecordingProgress();

        var summary = await CreateEngine()
            .RequestRecomputeAllAsync(RecomputeReason.RecomputeAll, collector)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, summary.Total);
        Assert.Contains(new SessionRecomputeAllProgress(0, 2), collector.Reports);
        Assert.Contains(collector.Reports, report => report is { Completed: 2, Total: 2 });
        Assert.All(collector.Reports, report => Assert.Equal(2, report.Total));
    }

    private sealed class RecordingProgress : IProgress<SessionRecomputeAllProgress>
    {
        private readonly object gate = new();
        public List<SessionRecomputeAllProgress> Reports { get; } = [];

        public void Report(SessionRecomputeAllProgress value)
        {
            lock (gate)
            {
                Reports.Add(value);
            }
        }
    }
}
