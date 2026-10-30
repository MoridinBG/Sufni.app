using System.Linq;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Doubles;
using System.Reactive.Linq;
using DynamicData;
using NSubstitute;
using Sufni.App.Shared.Stores;

using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.App.Tests.TestSupport.Fixtures;
using RecordedSessionProjectionModel = Sufni.App.Sessions.Processing.RecordedSessionProjection.RecordedSessionProjection;
namespace Sufni.App.Tests.Sessions.Processing.RecordedSessionProjection;

public class RecordedSessionProjectionTests
{
    [Fact]
    public async Task ConnectSessions_BikeEdit_PublishesAllAffectedSummariesInOneUpdate()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprints = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprints, scheduler);
        var first = CreateCurrentContext(fingerprints);
        var secondId = Guid.NewGuid();
        var second = first with
        {
            Session = first.Session with { Id = secondId },
            Source = first.Source with { SessionId = secondId }
        };
        second = second with { Session = second.Session with
        {
            ProcessingFingerprintJson = AppJson.Serialize(fingerprints.CreateCurrent(second.Session, second.Setup, second.Bike, second.Source))
        }};
        var updates = new List<RecordedSessionSummary[]>();
        using var observation = projection.ConnectSessions().Bind(out var summaries)
            .Subscribe(_ => updates.Add(summaries.ToArray()));
        await stores.AddAsync(first);
        await stores.AddAsync(second);
        await scheduler.DrainAsync();
        updates.Clear();

        await stores.Bikes.PublishBikesChangedAsync([first.Bike with { HeadAngle = first.Bike.HeadAngle + 1 }]);
        Assert.All(summaries, summary => Assert.IsType<SessionStaleness.Current>(summary.Staleness));
        await scheduler.DrainAsync();

        var published = Assert.Single(updates);
        Assert.Equal(2, published.Length);
        Assert.All(published, summary => Assert.IsType<SessionStaleness.Stale>(summary.Staleness));
        await scheduler.DrainAsync();
        Assert.Single(updates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingEvaluation_DoesNotPublishReplacedOrDeletedInputs(bool remove)
    {
        var scheduler = new ManualUiThreadDispatcher();
        var runner = new DeferredBackgroundTaskRunner();
        using var stores = new StoreFixtures();
        var fingerprints = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprints, scheduler, runner);
        var context = CreateCurrentContext(fingerprints);
        using var observation = projection.ConnectSessions().Bind(out var summaries).Subscribe();
        var completed = false;
        var domains = new List<RecordedSessionDomainSnapshot>();
        using var watch = projection.WatchSession(context.Session.Id).Subscribe(domains.Add, () => completed = true);
        await stores.AddAsync(context);
        await scheduler.DrainAsync();
        await CompleteEvaluationAsync(runner, scheduler);
        domains.Clear();

        await stores.Sessions.PublishSessionsChangedAsync([context.Session with { Name = "obsolete" }]);
        await scheduler.DrainAsync();
        Assert.Equal(context.Session.Name, Assert.Single(summaries).Name);
        if (remove)
        {
            await stores.Sessions.PublishSessionsRemovedAsync([context.Session.Id]);
            Assert.Empty(summaries);
            Assert.True(completed);
        }
        else
            await stores.Sessions.PublishSessionsChangedAsync([context.Session with { Name = "latest" }]);
        await CompleteEvaluationAsync(runner, scheduler);
        Assert.Empty(domains);

        if (remove) Assert.Empty(summaries);
        else
        {
            Assert.Equal(context.Session.Name, Assert.Single(summaries).Name);
            await CompleteEvaluationAsync(runner, scheduler);
            Assert.Equal("latest", Assert.Single(domains).Session.Name);
            Assert.Equal("latest", Assert.Single(summaries).Name);
        }
    }

    [Fact]
    public async Task Evaluation_UsesCapturedInputsOutsideTheUiDispatcher()
    {
        var scheduler = new ManualUiThreadDispatcher();
        var runner = new DeferredBackgroundTaskRunner();
        using var stores = new StoreFixtures();
        var actual = new ProcessingFingerprintService();
        var context = CreateCurrentContext(actual);
        var fingerprints = Substitute.For<IProcessingFingerprintService>();
        fingerprints.EvaluateState(Arg.Any<SessionSnapshot>(), Arg.Any<SetupSnapshot?>(), Arg.Any<BikeSnapshot?>(),
            Arg.Any<RecordedSessionSourceSnapshot?>(), Arg.Any<string?>()).Returns(call =>
        {
            Assert.False(scheduler.CheckAccess());
            return actual.EvaluateState(call.Arg<SessionSnapshot>(), call.Arg<SetupSnapshot?>(),
                call.Arg<BikeSnapshot?>(), call.Arg<RecordedSessionSourceSnapshot?>(), call.Arg<string?>());
        });
        using var projection = stores.CreateProjection(fingerprints, scheduler, runner);
        using var observation = projection.ConnectSessions().Bind(out var summaries).Subscribe();
        await stores.AddAsync(context);
        await scheduler.DrainAsync();
        Assert.Empty(summaries);

        await CompleteEvaluationAsync(runner, scheduler);

        Assert.IsType<SessionStaleness.Current>(Assert.Single(summaries).Staleness);
    }

    [Fact]
    public async Task WatchSession_ResubscriptionReplaysLatestDomain_AndKeepsOtherObserversActive()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprints = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprints, scheduler);
        var context = CreateCurrentContext(fingerprints);
        await stores.AddAsync(context);
        await scheduler.DrainAsync();
        var observable = projection.WatchSession(context.Session.Id);
        var first = new List<RecordedSessionDomainSnapshot>();
        using (observable.Subscribe(first.Add)) Assert.Single(first);
        await stores.Sessions.PublishSessionsChangedAsync([context.Session with { Name = "changed while closed" }]);
        await scheduler.DrainAsync();
        var reopened = new List<RecordedSessionDomainSnapshot>();
        using var reopenedWatch = observable.Subscribe(reopened.Add);
        var other = new List<RecordedSessionDomainSnapshot>();
        using (observable.Subscribe(other.Add)) Assert.Single(other);

        await stores.Sessions.PublishSessionsChangedAsync([context.Session with { Name = "latest" }]);
        await scheduler.DrainAsync();

        Assert.Equal("changed while closed", reopened[0].Session.Name);
        Assert.Equal("latest", reopened[1].Session.Name);
        Assert.Equal(2, reopened.Count);
        Assert.Single(first);
        Assert.Single(other);
    }

    private static async Task CompleteEvaluationAsync(DeferredBackgroundTaskRunner runner, ManualUiThreadDispatcher scheduler)
    {
        await runner.RunNextAsync();
        await scheduler.WaitForWorkAsync(global::Xunit.TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await scheduler.DrainAsync();
    }

    [Fact]
    public async Task ConnectSessions_PublishesCurrentSummary_WhenSessionIsFullyCurrent()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        using var subscription = projection.ConnectSessions().Bind(out var summaries).Subscribe();
        var context = CreateCurrentContext(fingerprintService);

        await stores.AddAsync(context);
        await scheduler.DrainAsync();

        var summary = Assert.Single(summaries);
        Assert.Equal(context.Session.Id, summary.Id);
        Assert.Equal(context.Session.Name, summary.Name);
        Assert.Equal(context.Session.HasProcessedData, summary.HasProcessedData);
        Assert.IsType<SessionStaleness.Current>(summary.Staleness);
    }

    [Fact]
    public async Task ConnectSessions_PublishesNoRawSummary_WhenSourceIsMissing()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        using var subscription = projection.ConnectSessions().Bind(out var summaries).Subscribe();
        var context = CreateCurrentContext(fingerprintService);

        await stores.Bikes.PublishBikesChangedAsync([context.Bike]);
        await stores.Setups.PublishSetupsChangedAsync([context.Setup]);
        await stores.Sessions.PublishSessionsChangedAsync([context.Session]);
        await scheduler.DrainAsync();

        var summary = Assert.Single(summaries);
        Assert.IsType<SessionStaleness.MissingRawSource>(summary.Staleness);
        Assert.False(summary.Staleness.IsStale);
    }

    [Fact]
    public async Task WatchSession_EmitsUpdatedSessionFacts_WhenSessionChanges()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        var context = CreateCurrentContext(fingerprintService);
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = projection.WatchSession(context.Session.Id).Subscribe(emissions.Add);

        await stores.AddAsync(context);
        await scheduler.DrainAsync();
        var initial = Assert.Single(emissions);
        Assert.Equal(context.Session, initial.Session);

        await stores.Sessions.PublishSessionsChangedAsync([context.Session with
        {
            Name = "renamed session",
            HasProcessedData = false,
            ProcessingFingerprintJson = null
        }]);
        await scheduler.DrainAsync();

        var latest = emissions.Last();
        Assert.Equal("renamed session", latest.Session.Name);
        Assert.False(latest.Session.HasProcessedData);
        Assert.Null(latest.Session.ProcessingFingerprintJson);
        Assert.IsType<SessionStaleness.Stale>(latest.Staleness);
    }

    [Fact]
    public async Task WatchSession_EmitsStaleSourceFacts_WhenSourceHashChanges()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        var context = CreateCurrentContext(fingerprintService);
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = projection.WatchSession(context.Session.Id).Subscribe(emissions.Add);
        await stores.AddAsync(context);
        await scheduler.DrainAsync();

        await stores.Sources.PublishSourcesChangedAsync([context.Source with { SourceHash = "changed-source-hash" }]);
        await scheduler.DrainAsync();

        var latest = emissions.Last();
        Assert.IsType<SessionStaleness.Stale>(latest.Staleness);
        Assert.Equal("changed-source-hash", latest.CurrentFingerprint!.SourceHash);
    }

    [Fact]
    public async Task WatchSession_EmitsUpdatedFingerprint_WhenDerivationWindowChanges()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        var context = CreateWindowedContext(fingerprintService);
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = projection.WatchSession(context.Session.Id).Subscribe(emissions.Add);
        await stores.AddAsync(context);
        await scheduler.DrainAsync();
        Assert.Single(emissions);
        emissions.Clear();

        await stores.Sessions.PublishSessionsChangedAsync([context.Session with { WindowStartSeconds = 2 }]);
        await scheduler.DrainAsync();

        var latest = Assert.Single(emissions);
        Assert.Equal(2, latest.Session.DerivationWindow!.StartSeconds);
        Assert.IsType<SessionStaleness.Stale>(latest.Staleness);
    }

    [Fact]
    public async Task WatchSession_RecomputesDerivedSession_WhenReferencedSourceHashChanges()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        var context = CreateWindowedContext(fingerprintService);
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = projection.WatchSession(context.Session.Id).Subscribe(emissions.Add);
        await stores.AddAsync(context);
        await scheduler.DrainAsync();
        Assert.Single(emissions);
        emissions.Clear();

        await stores.Sources.PublishSourcesChangedAsync([context.Source with { SourceHash = "changed-source-hash" }]);
        await scheduler.DrainAsync();

        var latest = Assert.Single(emissions);
        Assert.Equal(context.Session.Id, latest.Session.Id);
        Assert.Equal("changed-source-hash", latest.CurrentFingerprint!.SourceHash);
        Assert.IsType<SessionStaleness.Stale>(latest.Staleness);
    }

    [Fact]
    public async Task WatchSession_EmitsUpdatedDependency_WhenBikeProcessingDependencyChanges()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        var context = CreateCurrentContext(fingerprintService);
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = projection.WatchSession(context.Session.Id).Subscribe(emissions.Add);
        await stores.AddAsync(context);
        await scheduler.DrainAsync();

        await stores.Bikes.PublishBikesChangedAsync([context.Bike with { HeadAngle = context.Bike.HeadAngle + 0.5 }]);
        await scheduler.DrainAsync();

        var latest = emissions.Last();
        Assert.IsType<SessionStaleness.Stale>(latest.Staleness);
    }

    [Fact]
    public async Task WatchSession_DoesNotEmit_WhenBikeNonProcessingFieldsChange()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        var context = CreateCurrentContext(fingerprintService);
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = projection.WatchSession(context.Session.Id).Subscribe(emissions.Add);
        await stores.AddAsync(context);
        await scheduler.DrainAsync();
        Assert.Single(emissions);
        emissions.Clear();

        await stores.Bikes.PublishBikesChangedAsync([context.Bike with
        {
            Name = "renamed bike",
            ImageBytes = [9, 8, 7],
            Updated = context.Bike.Updated + 1
        }]);
        await scheduler.DrainAsync();

        Assert.Empty(emissions);
    }

    [Fact]
    public async Task WatchSession_CoalescesSameTurnStoreChangesIntoOneDomain()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        var context = CreateCurrentContext(fingerprintService);
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = projection.WatchSession(context.Session.Id).Subscribe(emissions.Add);
        await stores.AddAsync(context);
        await scheduler.DrainAsync();
        Assert.Single(emissions);
        emissions.Clear();

        await stores.Sessions.PublishSessionsChangedAsync([context.Session with { Name = "renamed session" }]);
        await stores.Bikes.PublishBikesChangedAsync([context.Bike with { HeadAngle = context.Bike.HeadAngle + 0.5 }]);
        await stores.Sources.PublishSourcesChangedAsync([context.Source with { SourceHash = "changed-source-hash" }]);

        Assert.Empty(emissions);
        await scheduler.DrainAsync();

        var latest = Assert.Single(emissions);
        Assert.Equal("renamed session", latest.Session.Name);
        Assert.Equal(context.Bike.HeadAngle + 0.5, latest.Bike!.HeadAngle);
        Assert.Equal("changed-source-hash", latest.Source!.SourceHash);
        Assert.IsType<SessionStaleness.Stale>(latest.Staleness);
    }

    [Fact]
    public async Task ConnectSessions_RemovesSummary_WhenSessionIsRemoved()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        using var subscription = projection.ConnectSessions().Bind(out var summaries).Subscribe();
        var context = CreateCurrentContext(fingerprintService);
        await stores.AddAsync(context);
        await scheduler.DrainAsync();
        Assert.Single(summaries);

        await stores.Sessions.PublishSessionsRemovedAsync([context.Session.Id]);

        Assert.Empty(summaries);
    }

    [Fact]
    public async Task WatchSession_CompletesAndStopsWatching_WhenSessionIsRemoved()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        var context = CreateCurrentContext(fingerprintService);
        var completed = false;
        var emissions = 0;
        using var subscription = projection.WatchSession(context.Session.Id).Subscribe(
            _ => emissions++,
            () => completed = true);
        await stores.AddAsync(context);
        await scheduler.DrainAsync();
        Assert.Equal(1, emissions);

        await stores.Sessions.PublishSessionsRemovedAsync([context.Session.Id]);

        Assert.True(completed);
        await stores.Sources.PublishSourcesChangedAsync([context.Source with { SourceHash = "changed-after-remove" }]);
        await scheduler.DrainAsync();
        Assert.Equal(1, emissions);
    }

    [Fact]
    public async Task WatchSession_StaysSubscribed_WhenRefreshUpdatesSameSession()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        var context = CreateCurrentContext(fingerprintService);
        var completed = false;
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = projection.WatchSession(context.Session.Id).Subscribe(
            emissions.Add,
            () => completed = true);
        await stores.AddAsync(context);
        await scheduler.DrainAsync();
        Assert.Single(emissions);

        var refreshed = context.Session with { Name = "refreshed session" };
        await stores.Sessions.ReplaceAllAsync([refreshed]);
        await scheduler.DrainAsync();

        Assert.False(completed);
        Assert.Equal(2, emissions.Count);
        Assert.Equal("refreshed session", emissions[^1].Session.Name);

        await stores.Sources.PublishSourcesChangedAsync([context.Source with { SourceHash = "changed-after-refresh" }]);
        await scheduler.DrainAsync();

        Assert.Equal(3, emissions.Count);
    }

    private static TestContext CreateCurrentContext(ProcessingFingerprintService fingerprintService)
    {
        var bike = TestSnapshots.Bike(id: Guid.NewGuid(), name: "projection bike");
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "projection setup", bikeId: bike.Id);
        var session = TestSnapshots.Session(
            id: Guid.NewGuid(),
            name: "projection session",
            setupId: setup.Id,
            hasProcessedData: true);
        var source = CreateSource(session.Id);
        var fingerprint = fingerprintService.CreateCurrent(session, setup, bike, source);
        session = session with { ProcessingFingerprintJson = AppJson.Serialize(fingerprint) };
        return new TestContext(session, setup, bike, source);
    }

    private static TestContext CreateWindowedContext(ProcessingFingerprintService fingerprintService)
    {
        var bike = TestSnapshots.Bike(id: Guid.NewGuid(), name: "projection bike");
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "projection setup", bikeId: bike.Id);
        var session = TestSnapshots.Session(
            id: Guid.NewGuid(),
            name: "projection derived session",
            setupId: setup.Id,
            hasProcessedData: true) with
        {
            SourceSessionId = Guid.NewGuid(),
            WindowStartSeconds = 1,
            WindowEndSeconds = 5
        };
        var source = CreateSource(session.SourceSessionId!.Value);
        var fingerprint = fingerprintService.CreateCurrent(session, setup, bike, source);
        session = session with { ProcessingFingerprintJson = AppJson.Serialize(fingerprint) };
        return new TestContext(session, setup, bike, source);
    }

    private static RecordedSessionSourceSnapshot CreateSource(Guid sessionId)
    {
        var payload = new byte[] { 7, 6, 5, 4 };
        var hash = RecordedSessionSourceHash.ComputeImportedSst(payload);
        return new RecordedSessionSourceSnapshot(
            sessionId,
            RecordedSessionSourceKind.ImportedSst,
            "projection.SST",
            1,
            hash);
    }

    private sealed record TestContext(
        SessionSnapshot Session,
        SetupSnapshot Setup,
        BikeSnapshot Bike,
        RecordedSessionSourceSnapshot Source);

    private sealed class StoreFixtures : IDisposable
    {
        public SessionStore Sessions { get; }
        public SetupStore Setups { get; }
        public BikeStore Bikes { get; }
        public RecordedSessionSourceStore Sources { get; }
        public ProcessingDependencyHashIndex DependencyHashIndex { get; }
        public StorePublication Publication { get; } = new(new InlineUiThreadDispatcher());

        public StoreFixtures()
        {
            Sessions = new SessionStore(Publication);
            Setups = new SetupStore(Publication);
            Bikes = new BikeStore(Publication);
            Sources = new RecordedSessionSourceStore(Publication);
            DependencyHashIndex = new ProcessingDependencyHashIndex(Setups, Bikes);
        }

        public RecordedSessionProjectionModel CreateProjection(
            IProcessingFingerprintService fingerprintService,
            IUiThreadDispatcher scheduler, IBackgroundTaskRunner? runner = null) => new(
            Sessions,
            Setups,
            Bikes,
            Sources,
            fingerprintService,
            DependencyHashIndex,
            scheduler,
            runner ?? new InlineBackgroundTaskRunner(),
            Publication,
            new UiThreadRecordedSessionProjectionScheduler(scheduler));

        public async Task AddAsync(TestContext context)
        {
            using var batch = Publication.BeginBatch();
            await Bikes.PublishBikesChangedAsync([context.Bike]);
            await Setups.PublishSetupsChangedAsync([context.Setup]);
            await Sources.PublishSourcesChangedAsync([context.Source]);
            await Sessions.PublishSessionsChangedAsync([context.Session]);
        }

        public void Dispose()
        {
            DependencyHashIndex.Dispose();
            Sessions.Dispose();
            Setups.Dispose();
            Bikes.Dispose();
            Sources.Dispose();
            Publication.Dispose();
        }
    }

}
