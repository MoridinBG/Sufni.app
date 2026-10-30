using System.Linq;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Doubles;
using System.Reactive.Linq;
using DynamicData;

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
    public async Task ConnectSessions_PublishesCurrentSummary_WhenSessionIsFullyCurrent()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        using var subscription = projection.ConnectSessions().Bind(out var summaries).Subscribe();
        var context = CreateCurrentContext(fingerprintService);

        stores.Add(context);
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

        stores.Bikes.Add(context.Bike);
        stores.Setups.Add(context.Setup);
        stores.Sessions.Add(context.Session);
        await scheduler.DrainAsync();

        var summary = Assert.Single(summaries);
        Assert.IsType<SessionStaleness.MissingRawSource>(summary.Staleness);
        Assert.False(summary.Staleness.IsStale);
    }

    [Fact]
    public async Task WatchSession_EmitsInitialThenCombinedSessionChangeKind_WhenSessionChanges()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        var context = CreateCurrentContext(fingerprintService);
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = projection.WatchSession(context.Session.Id).Subscribe(emissions.Add);

        stores.Add(context);
        await scheduler.DrainAsync();
        var initial = Assert.Single(emissions);
        Assert.Equal(DerivedChangeKind.Initial, initial.ChangeKind);

        stores.Sessions.Add(context.Session with
        {
            Name = "renamed session",
            HasProcessedData = false,
            ProcessingFingerprintJson = null
        });
        await scheduler.DrainAsync();

        var latest = emissions.Last();
        Assert.True(latest.ChangeKind.HasFlag(DerivedChangeKind.SessionMetadataChanged));
        Assert.True(latest.ChangeKind.HasFlag(DerivedChangeKind.ProcessedDataAvailabilityChanged));
        Assert.True(latest.ChangeKind.HasFlag(DerivedChangeKind.FingerprintChanged));
        Assert.IsType<SessionStaleness.Stale>(latest.Staleness);
    }

    [Fact]
    public async Task WatchSession_EmitsSourceAvailabilityChanged_WhenSourceHashChanges()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        var context = CreateCurrentContext(fingerprintService);
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = projection.WatchSession(context.Session.Id).Subscribe(emissions.Add);
        stores.Add(context);
        await scheduler.DrainAsync();

        stores.Sources.Add(context.Source with { SourceHash = "changed-source-hash" });
        await scheduler.DrainAsync();

        var latest = emissions.Last();
        Assert.Equal(DerivedChangeKind.SourceAvailabilityChanged, latest.ChangeKind);
        Assert.IsType<SessionStaleness.Stale>(latest.Staleness);
        Assert.Equal("changed-source-hash", latest.CurrentFingerprint!.SourceHash);
    }

    [Fact]
    public async Task WatchSession_EmitsFingerprintChanged_WhenDerivationWindowChanges()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        var context = CreateWindowedContext(fingerprintService);
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = projection.WatchSession(context.Session.Id).Subscribe(emissions.Add);
        stores.Add(context);
        await scheduler.DrainAsync();
        Assert.Single(emissions);
        emissions.Clear();

        stores.Sessions.Add(context.Session with { WindowStartSeconds = 2 });
        await scheduler.DrainAsync();

        var latest = Assert.Single(emissions);
        Assert.Equal(2, latest.Session.DerivationWindow!.StartSeconds);
        Assert.Equal(DerivedChangeKind.FingerprintChanged, latest.ChangeKind);
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
        stores.Add(context);
        await scheduler.DrainAsync();
        Assert.Single(emissions);
        emissions.Clear();

        stores.Sources.Add(context.Source with { SourceHash = "changed-source-hash" });
        await scheduler.DrainAsync();

        var latest = Assert.Single(emissions);
        Assert.Equal(context.Session.Id, latest.Session.Id);
        Assert.Equal("changed-source-hash", latest.CurrentFingerprint!.SourceHash);
        Assert.True(latest.ChangeKind.HasFlag(DerivedChangeKind.SourceAvailabilityChanged));
        Assert.IsType<SessionStaleness.Stale>(latest.Staleness);
    }

    [Fact]
    public async Task WatchSession_EmitsDependencyChanged_WhenBikeProcessingDependencyChanges()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        var context = CreateCurrentContext(fingerprintService);
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = projection.WatchSession(context.Session.Id).Subscribe(emissions.Add);
        stores.Add(context);
        await scheduler.DrainAsync();

        stores.Bikes.Add(context.Bike with { HeadAngle = context.Bike.HeadAngle + 0.5 });
        await scheduler.DrainAsync();

        var latest = emissions.Last();
        Assert.Equal(DerivedChangeKind.DependencyChanged, latest.ChangeKind);
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
        stores.Add(context);
        await scheduler.DrainAsync();
        Assert.Single(emissions);
        emissions.Clear();

        stores.Bikes.Add(context.Bike with
        {
            Name = "renamed bike",
            ImageBytes = [9, 8, 7],
            Updated = context.Bike.Updated + 1
        });
        await scheduler.DrainAsync();

        Assert.Empty(emissions);
    }

    [Fact]
    public async Task WatchSession_CoalescesSameTurnStoreChangesIntoSingleMultiCauseEmission()
    {
        var scheduler = new ManualUiThreadDispatcher();
        using var stores = new StoreFixtures();
        var fingerprintService = new ProcessingFingerprintService();
        using var projection = stores.CreateProjection(fingerprintService, scheduler);
        var context = CreateCurrentContext(fingerprintService);
        var emissions = new List<RecordedSessionDomainSnapshot>();
        using var subscription = projection.WatchSession(context.Session.Id).Subscribe(emissions.Add);
        stores.Add(context);
        await scheduler.DrainAsync();
        Assert.Single(emissions);
        emissions.Clear();

        stores.Sessions.Add(context.Session with { Name = "renamed session" });
        stores.Bikes.Add(context.Bike with { HeadAngle = context.Bike.HeadAngle + 0.5 });
        stores.Sources.Add(context.Source with { SourceHash = "changed-source-hash" });

        Assert.Empty(emissions);
        await scheduler.DrainAsync();

        var latest = Assert.Single(emissions);
        var expected =
            DerivedChangeKind.SessionMetadataChanged |
            DerivedChangeKind.SourceAvailabilityChanged |
            DerivedChangeKind.DependencyChanged;
        Assert.Equal(expected, latest.ChangeKind);
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
        stores.Add(context);
        await scheduler.DrainAsync();
        Assert.Single(summaries);

        stores.Sessions.Remove(context.Session.Id);

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
        stores.Add(context);
        await scheduler.DrainAsync();
        Assert.Equal(1, emissions);

        stores.Sessions.Remove(context.Session.Id);

        Assert.True(completed);
        stores.Sources.Add(context.Source with { SourceHash = "changed-after-remove" });
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
        stores.Add(context);
        await scheduler.DrainAsync();
        Assert.Single(emissions);

        var refreshed = context.Session with { Name = "refreshed session" };
        stores.Sessions.RefreshWith(refreshed);
        await scheduler.DrainAsync();

        Assert.False(completed);
        Assert.Equal(2, emissions.Count);
        Assert.Equal("refreshed session", emissions[^1].Session.Name);
        Assert.Equal(DerivedChangeKind.SessionMetadataChanged, emissions[^1].ChangeKind);

        stores.Sources.Add(context.Source with { SourceHash = "changed-after-refresh" });
        await scheduler.DrainAsync();

        Assert.Equal(3, emissions.Count);
        Assert.Equal(DerivedChangeKind.SourceAvailabilityChanged, emissions[^1].ChangeKind);
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
        public SessionStoreStub Sessions { get; } = new();
        public SetupStoreStub Setups { get; } = new();
        public BikeStoreStub Bikes { get; } = new();
        public RecordedSourceStoreStub Sources { get; } = new();
        public ProcessingDependencyHashIndex DependencyHashIndex { get; }

        public StoreFixtures()
        {
            DependencyHashIndex = new ProcessingDependencyHashIndex(Setups, Bikes);
        }

        public RecordedSessionProjectionModel CreateProjection(
            IProcessingFingerprintService fingerprintService,
            IUiThreadDispatcher scheduler) => new(
            Sessions,
            Setups,
            Bikes,
            Sources,
            fingerprintService,
            DependencyHashIndex,
            new UiThreadRecordedSessionProjectionScheduler(scheduler));

        public void Add(TestContext context)
        {
            Bikes.Add(context.Bike);
            Setups.Add(context.Setup);
            Sources.Add(context.Source);
            Sessions.Add(context.Session);
        }

        public void Dispose()
        {
            DependencyHashIndex.Dispose();
            Sessions.Dispose();
            Setups.Dispose();
            Bikes.Dispose();
            Sources.Dispose();
        }
    }

}
