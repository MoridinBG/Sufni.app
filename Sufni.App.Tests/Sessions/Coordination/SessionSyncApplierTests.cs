using Sufni.App.Shared.Models;
using NSubstitute;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Async;

namespace Sufni.App.Tests.Sessions.Coordination;

public class SessionSyncApplierTests
{
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ISessionStoreWriter sessionStore = Substitute.For<ISessionStoreWriter>();
    private readonly IRecordedSessionSourceRepository sourceRepository = Substitute.For<IRecordedSessionSourceRepository>();
    private readonly IRecordedSessionSourceStoreWriter sourceStore = Substitute.For<IRecordedSessionSourceStoreWriter>();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DataArrived_PublishesOnUiDispatcher(bool recordedSource)
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        var dispatcher = new ManualUiThreadDispatcher();
        _ = new SessionSyncApplier(sessionStore, sourceStore, dispatcher, sourceRepository, sessionRepository, new InlineBackgroundTaskRunner(), sync);
        var sessionId = Guid.NewGuid();
        sessionRepository.GetSessionAsync(sessionId).Returns(new Session(sessionId, "current", "", null));
        sourceRepository.GetRecordedSessionSourceSnapshotAsync(sessionId).Returns(RecordedSessionSourceSnapshot.From(PersistenceTestData.CreateRecordedSessionSource(sessionId)));

        if (recordedSource)
            sync.SessionSourceDataArrived += Raise.EventWith(sync, new SessionDataArrivedEventArgs(sessionId));
        else
            sync.SessionDataArrived += Raise.EventWith(sync, new SessionDataArrivedEventArgs(sessionId));

        await sessionStore.DidNotReceive().PublishSessionsChangedAsync(Arg.Any<IReadOnlyCollection<SessionSnapshot>>(), Arg.Any<CancellationToken>());
        await sourceStore.DidNotReceive().PublishSourcesChangedAsync(Arg.Any<IReadOnlyCollection<RecordedSessionSourceSnapshot>>(), Arg.Any<CancellationToken>());
        await dispatcher.DrainAsync();

        if (recordedSource)
            await sourceStore.Received(1).PublishSourcesChangedAsync(
                Arg.Is<IReadOnlyCollection<RecordedSessionSourceSnapshot>>(ids => ids.Count == 1 && ids.Select(snapshot => snapshot.SessionId).Contains(sessionId)),
                Arg.Any<CancellationToken>());
        else
            await sessionStore.Received(1).PublishSessionsChangedAsync(
                Arg.Is<IReadOnlyCollection<SessionSnapshot>>(ids => ids.Count == 1 && ids.Select(snapshot => snapshot.Id).Contains(sessionId)),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncApplier_PublishesDeletedAndLiveSessions()
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        var dispatcher = new ManualUiThreadDispatcher();
        _ = new SessionSyncApplier(sessionStore, sourceStore, dispatcher, sourceRepository, sessionRepository, new InlineBackgroundTaskRunner(), sync);
        var liveId = Guid.NewGuid();
        sessionRepository.GetSessionAsync(liveId).Returns(new Session(liveId, "current", "", null));
        var deletedId = Guid.NewGuid();
        var data = new SynchronizationData
        {
            Sessions =
            {
                new Session { Id = liveId, Updated = 6 },
                new Session { Id = deletedId, Updated = 6, Deleted = 6 },
            },
        };

        sync.SynchronizationDataArrived += Raise.EventWith(sync, new SynchronizationDataArrivedEventArgs(data));
        await dispatcher.DrainAsync();

        await sessionStore.Received(1).PublishSessionsRemovedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(deletedId)),
            Arg.Any<CancellationToken>());
        await sessionStore.Received(1).PublishSessionsChangedAsync(
            Arg.Is<IReadOnlyCollection<SessionSnapshot>>(ids => ids.Count == 1 && ids.Select(snapshot => snapshot.Id).Contains(liveId)),
            Arg.Any<CancellationToken>());
    }

}
