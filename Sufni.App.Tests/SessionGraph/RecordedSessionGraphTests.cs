using System.Linq;
using System.Reactive.Linq;
using DynamicData;
using Sufni.App.Models;
using Sufni.App.SessionGraph;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.SessionGraph;

public class RecordedSessionGraphTests
{
    [Fact]
    public void ConnectSessions_PublishesCurrentSummary_WhenSessionIsFullyCurrent()
    {
        using var synchronization = new QueuedSynchronizationContextScope();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var graph = stores.CreateGraph(fingerprintService);
        using var subscription = graph.ConnectSessions().Bind(out var summaries).Subscribe();
        var context = CreateCurrentContext(fingerprintService);

        stores.Add(context);
        synchronization.Flush();

        var summary = Assert.Single(summaries);
        Assert.Equal(context.Session.Id, summary.Id);
        Assert.Equal(context.Session.Name, summary.Name);
        Assert.Equal(context.Session.HasProcessedData, summary.HasProcessedData);
        Assert.IsType<SessionStaleness.Current>(summary.Staleness);
    }

    [Fact]
    public void WatchSession_EmitsInitialThenCombinedSessionChangeKind_WhenSessionChanges()
    {
        using var synchronization = new QueuedSynchronizationContextScope();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var graph = stores.CreateGraph(fingerprintService);
        var context = CreateCurrentContext(fingerprintService);
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = graph.WatchSession(context.Session.Id).Subscribe(emissions.Add);

        stores.Add(context);
        synchronization.Flush();
        var initial = Assert.Single(emissions);
        Assert.Equal(DerivedChangeKind.Initial, initial.ChangeKind);

        stores.Sessions.Add(context.Session with
        {
            Name = "renamed session",
            HasProcessedData = false,
            ProcessingFingerprintJson = null
        });
        synchronization.Flush();

        var latest = emissions.Last();
        Assert.True(latest.ChangeKind.HasFlag(DerivedChangeKind.SessionMetadataChanged));
        Assert.True(latest.ChangeKind.HasFlag(DerivedChangeKind.ProcessedDataAvailabilityChanged));
        Assert.True(latest.ChangeKind.HasFlag(DerivedChangeKind.FingerprintChanged));
        Assert.IsType<SessionStaleness.MissingProcessedData>(latest.Staleness);
    }

    [Fact]
    public void WatchSession_EmitsSourceAvailabilityChanged_WhenSourceHashChanges()
    {
        using var synchronization = new QueuedSynchronizationContextScope();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var graph = stores.CreateGraph(fingerprintService);
        var context = CreateCurrentContext(fingerprintService);
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = graph.WatchSession(context.Session.Id).Subscribe(emissions.Add);
        stores.Add(context);
        synchronization.Flush();

        stores.Sources.Add(context.Source with { SourceHash = "changed-source-hash" });
        synchronization.Flush();

        var latest = emissions.Last();
        Assert.Equal(DerivedChangeKind.SourceAvailabilityChanged, latest.ChangeKind);
        Assert.IsType<SessionStaleness.DependencyHashChanged>(latest.Staleness);
        Assert.Equal("changed-source-hash", latest.CurrentFingerprint!.SourceHash);
    }

    [Fact]
    public void WatchSession_EmitsDependencyChanged_WhenBikeProcessingDependencyChanges()
    {
        using var synchronization = new QueuedSynchronizationContextScope();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var graph = stores.CreateGraph(fingerprintService);
        var context = CreateCurrentContext(fingerprintService);
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = graph.WatchSession(context.Session.Id).Subscribe(emissions.Add);
        stores.Add(context);
        synchronization.Flush();

        stores.Bikes.Add(context.Bike with { HeadAngle = context.Bike.HeadAngle + 0.5 });
        synchronization.Flush();

        var latest = emissions.Last();
        Assert.Equal(DerivedChangeKind.DependencyChanged, latest.ChangeKind);
        Assert.IsType<SessionStaleness.DependencyHashChanged>(latest.Staleness);
    }

    [Fact]
    public void WatchSession_CoalescesSameTurnStoreChangesIntoSingleMultiCauseEmission()
    {
        using var synchronization = new QueuedSynchronizationContextScope();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var graph = stores.CreateGraph(fingerprintService);
        var context = CreateCurrentContext(fingerprintService);
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = graph.WatchSession(context.Session.Id).Subscribe(emissions.Add);
        stores.Add(context);
        synchronization.Flush();
        Assert.Single(emissions);
        emissions.Clear();

        stores.Sessions.Add(context.Session with { Name = "renamed session" });
        stores.Bikes.Add(context.Bike with { HeadAngle = context.Bike.HeadAngle + 0.5 });
        stores.Sources.Add(context.Source with { SourceHash = "changed-source-hash" });

        Assert.Empty(emissions);
        synchronization.Flush();

        var latest = Assert.Single(emissions);
        var expected =
            DerivedChangeKind.SessionMetadataChanged |
            DerivedChangeKind.SourceAvailabilityChanged |
            DerivedChangeKind.DependencyChanged;
        Assert.Equal(expected, latest.ChangeKind);
        Assert.IsType<SessionStaleness.DependencyHashChanged>(latest.Staleness);
    }

    [Fact]
    public void ConnectSessions_RemovesSummary_WhenSessionIsRemoved()
    {
        using var synchronization = new QueuedSynchronizationContextScope();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var graph = stores.CreateGraph(fingerprintService);
        using var subscription = graph.ConnectSessions().Bind(out var summaries).Subscribe();
        var context = CreateCurrentContext(fingerprintService);
        stores.Add(context);
        synchronization.Flush();
        Assert.Single(summaries);

        stores.Sessions.Remove(context.Session.Id);

        Assert.Empty(summaries);
    }

    private static TestContext CreateCurrentContext(ProcessingFingerprintService fingerprintService)
    {
        var bike = TestSnapshots.Bike(id: Guid.NewGuid(), name: "graph bike");
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), name: "graph setup", bikeId: bike.Id);
        var session = TestSnapshots.Session(
            id: Guid.NewGuid(),
            name: "graph session",
            setupId: setup.Id,
            hasProcessedData: true);
        var source = CreateSource(session.Id);
        var fingerprint = fingerprintService.CreateCurrent(session, setup, bike, source);
        session = session with { ProcessingFingerprintJson = AppJson.Serialize(fingerprint) };
        return new TestContext(session, setup, bike, source);
    }

    private static RecordedSessionSourceSnapshot CreateSource(Guid sessionId)
    {
        var payload = new byte[] { 7, 6, 5, 4 };
        var hash = RecordedSessionSourceHash.Compute(
            RecordedSessionSourceKind.ImportedSst,
            "graph.SST",
            1,
            payload);
        return new RecordedSessionSourceSnapshot(
            sessionId,
            RecordedSessionSourceKind.ImportedSst,
            "graph.SST",
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
        public InMemorySessionStore Sessions { get; } = new();
        public InMemorySetupStore Setups { get; } = new();
        public InMemoryBikeStore Bikes { get; } = new();
        public InMemoryRecordedSourceStore Sources { get; } = new();

        public RecordedSessionGraph CreateGraph(IProcessingFingerprintService fingerprintService) => new(
            Sessions,
            Setups,
            Bikes,
            Sources,
            fingerprintService);

        public void Add(TestContext context)
        {
            Bikes.Add(context.Bike);
            Setups.Add(context.Setup);
            Sources.Add(context.Source);
            Sessions.Add(context.Session);
        }

        public void Dispose()
        {
            Sessions.Dispose();
            Setups.Dispose();
            Bikes.Dispose();
            Sources.Dispose();
        }
    }

    private sealed class InMemorySessionStore : ISessionStore, IDisposable
    {
        private readonly SourceCache<SessionSnapshot, Guid> cache = new(snapshot => snapshot.Id);

        public IObservable<IChangeSet<SessionSnapshot, Guid>> Connect() => cache.Connect();

        public IObservable<SessionSnapshot> Watch(Guid id) => Observable.Empty<SessionSnapshot>();

        public SessionSnapshot? Get(Guid id)
        {
            var result = cache.Lookup(id);
            return result.HasValue ? result.Value : null;
        }

        public Task RefreshAsync() => Task.CompletedTask;

        public void Add(SessionSnapshot snapshot) => cache.AddOrUpdate(snapshot);

        public void Remove(Guid id) => cache.RemoveKey(id);

        public void Dispose() => cache.Dispose();
    }

    private sealed class InMemorySetupStore : ISetupStore, IDisposable
    {
        private readonly SourceCache<SetupSnapshot, Guid> cache = new(snapshot => snapshot.Id);

        public IObservable<IChangeSet<SetupSnapshot, Guid>> Connect() => cache.Connect();

        public SetupSnapshot? Get(Guid id)
        {
            var result = cache.Lookup(id);
            return result.HasValue ? result.Value : null;
        }

        public SetupSnapshot? FindByBoardId(Guid boardId) =>
            cache.Items.FirstOrDefault(snapshot => snapshot.BoardId == boardId);

        public Task RefreshAsync() => Task.CompletedTask;

        public void Add(SetupSnapshot snapshot) => cache.AddOrUpdate(snapshot);

        public void Dispose() => cache.Dispose();
    }

    private sealed class InMemoryBikeStore : IBikeStore, IDisposable
    {
        private readonly SourceCache<BikeSnapshot, Guid> cache = new(snapshot => snapshot.Id);

        public IObservable<IChangeSet<BikeSnapshot, Guid>> Connect() => cache.Connect();

        public BikeSnapshot? Get(Guid id)
        {
            var result = cache.Lookup(id);
            return result.HasValue ? result.Value : null;
        }

        public Task RefreshAsync() => Task.CompletedTask;

        public void Add(BikeSnapshot snapshot) => cache.AddOrUpdate(snapshot);

        public void Dispose() => cache.Dispose();
    }

    private sealed class InMemoryRecordedSourceStore : IRecordedSessionSourceStore, IDisposable
    {
        private readonly SourceCache<RecordedSessionSourceSnapshot, Guid> cache = new(snapshot => snapshot.SessionId);

        public IObservable<IChangeSet<RecordedSessionSourceSnapshot, Guid>> Connect() => cache.Connect();

        public RecordedSessionSourceSnapshot? Get(Guid sessionId)
        {
            var result = cache.Lookup(sessionId);
            return result.HasValue ? result.Value : null;
        }

        public Task<RecordedSessionSource?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RecordedSessionSource?>(null);

        public Task RefreshAsync() => Task.CompletedTask;

        public void Add(RecordedSessionSourceSnapshot snapshot) => cache.AddOrUpdate(snapshot);

        public void Dispose() => cache.Dispose();
    }

    private sealed class QueuedSynchronizationContextScope : IDisposable
    {
        private readonly SynchronizationContext? previousContext;

        public QueuedSynchronizationContextScope()
        {
            previousContext = SynchronizationContext.Current;
            Context = new QueuedSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(Context);
        }

        public QueuedSynchronizationContext Context { get; }

        public void Flush() => Context.Flush();

        public void Dispose()
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> callbacks = new();

        public override void Post(SendOrPostCallback d, object? state) => callbacks.Enqueue((d, state));

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public void Flush()
        {
            while (callbacks.Count > 0)
            {
                var (callback, state) = callbacks.Dequeue();
                callback(state);
            }
        }
    }
}
