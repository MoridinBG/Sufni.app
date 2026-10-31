using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Stores;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Async;
using Microsoft.Extensions.Time.Testing;

namespace Sufni.App.Tests.Sessions.Coordination;

public class SessionSyncApplierTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Arrival_PublishesAuthoritativeSnapshotsOnUiDispatcher(bool recordedSource)
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        var dispatcher = new ManualUiThreadDispatcher();
        using var publication = new StorePublication(dispatcher);
        using var sessions = new SessionStore(publication);
        using var sources = new RecordedSessionSourceStore(publication);
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        var sourceRepository = Substitute.For<IRecordedSessionSourceRepository>();
        using var applier = new SessionSyncApplier(sessions, sources, dispatcher, sourceRepository, repository.Repository,
            new InlineBackgroundTaskRunner(), new SessionTelemetryProcessor(), Substitute.For<ISessionBlobSwapRequestStore>(), new ProcessingFingerprintService());
        applier.AttachServer(sync);
        var snapshot = TestSnapshots.Session();
        repository.Seed(snapshot);
        var source = RecordedSessionSourceSnapshot.From(PersistenceTestData.CreateRecordedSessionSource(snapshot.Id));
        sourceRepository.GetRecordedSessionSourceSnapshotAsync(snapshot.Id).Returns(source);

        if (recordedSource)
            sync.SessionSourceDataArrived += Raise.EventWith(sync, new SessionDataArrivedEventArgs(snapshot.Id));
        else
            sync.SynchronizationDataArrived += Raise.EventWith(sync, new SynchronizationDataArrivedEventArgs(
                new SynchronizationData { Sessions = [new Session { Id = snapshot.Id }] }));
        Assert.Null(sessions.Get(snapshot.Id));
        Assert.Null(sources.Get(snapshot.Id));
        await dispatcher.DrainAsync();

        if (recordedSource) Assert.Equal(source, sources.Get(snapshot.Id));
        else Assert.Equal(snapshot, sessions.Get(snapshot.Id));
    }

    [Fact]
    public async Task MetadataArrival_RemovesDeletedSession_AndPublishesRetainedDatabaseValues()
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        var dispatcher = new ManualUiThreadDispatcher();
        using var publication = new StorePublication(dispatcher);
        using var sessions = new SessionStore(publication);
        using var sources = new RecordedSessionSourceStore(publication);
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        using var applier = new SessionSyncApplier(sessions, sources, dispatcher, Substitute.For<IRecordedSessionSourceRepository>(),
            repository.Repository, new InlineBackgroundTaskRunner(), new SessionTelemetryProcessor(), Substitute.For<ISessionBlobSwapRequestStore>(), new ProcessingFingerprintService());
        applier.AttachServer(sync);
        var live = TestSnapshots.Session(updated: 8);
        var deleted = TestSnapshots.Session();
        repository.Seed(live);
        var seed = sessions.ReplaceAllAsync([deleted]);
        await dispatcher.DrainAsync();
        await seed;
        var data = new SynchronizationData { Sessions =
        [
            new Session { Id = live.Id, Updated = 6 },
            new Session { Id = deleted.Id, Updated = 6, Deleted = 6 }
        ] };

        sync.SynchronizationDataArrived += Raise.EventWith(sync, new SynchronizationDataArrivedEventArgs(data));
        await dispatcher.DrainAsync();

        Assert.Equal(live, sessions.Get(live.Id));
        Assert.Null(sessions.Get(deleted.Id));
    }
}
