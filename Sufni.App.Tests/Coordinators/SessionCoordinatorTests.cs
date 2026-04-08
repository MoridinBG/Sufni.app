using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.Coordinators;

public class SessionCoordinatorTests
{
    private readonly ISessionStoreWriter sessionStore = Substitute.For<ISessionStoreWriter>();
    private readonly IDatabaseService database = Substitute.For<IDatabaseService>();
    private readonly IHttpApiService http = Substitute.For<IHttpApiService>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();

    private SessionCoordinator CreateCoordinator(ISynchronizationServerService? sync = null) =>
        new(sessionStore, database, http, shell, dialogService, sync);

    // ----- OpenEditAsync -----

    [Fact]
    public async Task OpenEditAsync_NoOp_WhenSnapshotMissing()
    {
        sessionStore.Get(Arg.Any<Guid>()).Returns((SessionSnapshot?)null);

        await CreateCoordinator().OpenEditAsync(Guid.NewGuid());

        shell.DidNotReceiveWithAnyArgs().OpenOrFocus<SessionDetailViewModel>(default!, default!);
    }

    // OpenEditAsync's happy path constructs a SessionDetailViewModel,
    // which subscribes to NotesPage property-changed events on the
    // dispatcher thread — that needs the headless app, so it lives in
    // a separate [AvaloniaFact] below.
    [AvaloniaFact]
    public async Task OpenEditAsync_RoutesThroughOpenOrFocus_WhenSnapshotPresent()
    {
        var snapshot = TestSnapshots.Session();
        sessionStore.Get(snapshot.Id).Returns(snapshot);

        await CreateCoordinator().OpenEditAsync(snapshot.Id);

        shell.Received(1).OpenOrFocus(
            Arg.Any<Func<SessionDetailViewModel, bool>>(),
            Arg.Any<Func<SessionDetailViewModel>>());
    }

    // ----- SaveAsync -----

    [Fact]
    public async Task SaveAsync_HappyPath_WritesAndRefetchesAndUpserts()
    {
        var existing = TestSnapshots.Session(updated: 5);
        sessionStore.Get(existing.Id).Returns(existing);

        var session = new Session(existing.Id, "renamed", "", null) { Updated = 7 };
        var fresh = new Session(existing.Id, "renamed", "", null)
        {
            Updated = 7,
            HasProcessedData = true,
        };
        database.GetSessionAsync(existing.Id).Returns(fresh);

        var result = await CreateCoordinator().SaveAsync(session, baselineUpdated: 5);

        await database.Received(1).PutSessionAsync(session);
        await database.Received(1).GetSessionAsync(existing.Id);
        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(s =>
            s.Id == existing.Id && s.Name == "renamed" && s.Updated == 7 && s.HasProcessedData));
        var saved = Assert.IsType<SessionSaveResult.Saved>(result);
        Assert.Equal(7, saved.NewBaselineUpdated);
    }

    [Fact]
    public async Task SaveAsync_ReturnsFailed_WhenRefetchReturnsNull()
    {
        var existing = TestSnapshots.Session(updated: 5);
        sessionStore.Get(existing.Id).Returns(existing);
        database.GetSessionAsync(existing.Id).Returns((Session?)null);

        var session = new Session(existing.Id, "renamed", "", null);

        var result = await CreateCoordinator().SaveAsync(session, baselineUpdated: 5);

        var failed = Assert.IsType<SessionSaveResult.Failed>(result);
        Assert.Equal("Session disappeared after save", failed.ErrorMessage);
        sessionStore.DidNotReceive().Upsert(Arg.Any<SessionSnapshot>());
    }

    [Fact]
    public async Task SaveAsync_ReturnsConflict_WhenStoreIsNewer()
    {
        var current = TestSnapshots.Session(updated: 10);
        sessionStore.Get(current.Id).Returns(current);

        var session = new Session(current.Id, "stale", "", null);

        var result = await CreateCoordinator().SaveAsync(session, baselineUpdated: 5);

        var conflict = Assert.IsType<SessionSaveResult.Conflict>(result);
        Assert.Same(current, conflict.CurrentSnapshot);
        await database.DidNotReceive().PutSessionAsync(Arg.Any<Session>());
        sessionStore.DidNotReceive().Upsert(Arg.Any<SessionSnapshot>());
    }

    [Fact]
    public async Task SaveAsync_ReturnsFailed_WhenPutSessionThrows()
    {
        var existing = TestSnapshots.Session(updated: 5);
        sessionStore.Get(existing.Id).Returns(existing);
        database.PutSessionAsync(Arg.Any<Session>()).ThrowsAsync(new InvalidOperationException("disk full"));

        var session = new Session(existing.Id, "x", "", null);

        var result = await CreateCoordinator().SaveAsync(session, baselineUpdated: 5);

        var failed = Assert.IsType<SessionSaveResult.Failed>(result);
        Assert.Equal("disk full", failed.ErrorMessage);
        sessionStore.DidNotReceive().Upsert(Arg.Any<SessionSnapshot>());
    }

    // ----- DeleteAsync -----

    [Fact]
    public async Task DeleteAsync_HappyPath_DeletesClosesAndRemoves()
    {
        var id = Guid.NewGuid();

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.Equal(SessionDeleteOutcome.Deleted, result.Outcome);
        await database.Received(1).DeleteAsync<Session>(id);
        shell.Received(1).CloseIfOpen(Arg.Any<Func<SessionDetailViewModel, bool>>());
        sessionStore.Received(1).Remove(id);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFailed_WhenDatabaseDeleteThrows()
    {
        var id = Guid.NewGuid();
        database.DeleteAsync<Session>(id).ThrowsAsync(new InvalidOperationException("locked"));

        var result = await CreateCoordinator().DeleteAsync(id);

        Assert.Equal(SessionDeleteOutcome.Failed, result.Outcome);
        Assert.Equal("locked", result.ErrorMessage);
        sessionStore.DidNotReceiveWithAnyArgs().Remove(default);
        shell.DidNotReceiveWithAnyArgs().CloseIfOpen<SessionDetailViewModel>(default!);
    }

    // ----- EnsureTelemetryDataAvailableAsync -----

    [Fact]
    public async Task EnsureTelemetryDataAvailableAsync_NoOp_WhenAlreadyHasProcessedData()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        sessionStore.Get(snapshot.Id).Returns(snapshot);

        await CreateCoordinator().EnsureTelemetryDataAvailableAsync(snapshot.Id);

        await http.DidNotReceive().GetSessionPsstAsync(Arg.Any<Guid>());
        await database.DidNotReceive().PatchSessionPsstAsync(Arg.Any<Guid>(), Arg.Any<byte[]>());
    }

    [Fact]
    public async Task EnsureTelemetryDataAvailableAsync_PullsAndPatches_AndUpsertsRefetchedSnapshot()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        var psst = new byte[] { 1, 2, 3 };
        http.GetSessionPsstAsync(snapshot.Id).Returns(psst);
        var fresh = new Session(snapshot.Id, snapshot.Name, "", null)
        {
            Updated = 9,
            HasProcessedData = true,
        };
        database.GetSessionAsync(snapshot.Id).Returns(fresh);

        await CreateCoordinator().EnsureTelemetryDataAvailableAsync(snapshot.Id);

        await http.Received(1).GetSessionPsstAsync(snapshot.Id);
        await database.Received(1).PatchSessionPsstAsync(snapshot.Id, psst);
        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(s =>
            s.Id == snapshot.Id && s.HasProcessedData && s.Updated == 9));
    }

    [Fact]
    public async Task EnsureTelemetryDataAvailableAsync_Throws_WhenHttpReturnsNull()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        http.GetSessionPsstAsync(snapshot.Id).Returns((byte[]?)null);

        var ex = await Assert.ThrowsAsync<Exception>(() =>
            CreateCoordinator().EnsureTelemetryDataAvailableAsync(snapshot.Id));
        Assert.Equal("Session data could not be downloaded from server.", ex.Message);
        await database.DidNotReceive().PatchSessionPsstAsync(Arg.Any<Guid>(), Arg.Any<byte[]>());
    }

    // ----- Sync arrival handlers -----

    [AvaloniaFact]
    public async Task Constructor_SubscribesToSyncEvents_AndUpsertsOnSessionDataArrived()
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        var coordinator = CreateCoordinator(sync);

        var sessionId = Guid.NewGuid();
        var fresh = new Session(sessionId, "n", "", null) { Updated = 4, HasProcessedData = true };
        database.GetSessionAsync(sessionId).Returns(fresh);

        sync.SessionDataArrived += Raise.EventWith(sync, new SessionDataArrivedEventArgs(sessionId));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(s =>
            s.Id == sessionId && s.Updated == 4));
    }

    [AvaloniaFact]
    public async Task Constructor_OnSynchronizationDataArrived_RemovesDeletedSessionsAndUpsertsLive()
    {
        var sync = Substitute.For<ISynchronizationServerService>();
        var coordinator = CreateCoordinator(sync);

        var liveId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var fresh = new Session(liveId, "live", "", null) { Updated = 6 };
        database.GetSessionAsync(liveId).Returns(fresh);

        var data = new SynchronizationData
        {
            Sessions =
            {
                new Session { Id = liveId, Updated = 6 },
                new Session { Id = deletedId, Updated = 6, Deleted = 6 },
            },
        };

        sync.SynchronizationDataArrived += Raise.EventWith(sync, new SynchronizationDataArrivedEventArgs(data));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        sessionStore.Received(1).Remove(deletedId);
        sessionStore.Received(1).Upsert(Arg.Is<SessionSnapshot>(s => s.Id == liveId && s.Updated == 6));
    }

    [Fact]
    public void Constructor_DoesNotSubscribe_WhenSyncServerNull()
    {
        // Smoke test: with no sync server we should still get a working
        // coordinator and never NRE on construction.
        var coordinator = CreateCoordinator(sync: null);
        Assert.NotNull(coordinator);
    }
}
