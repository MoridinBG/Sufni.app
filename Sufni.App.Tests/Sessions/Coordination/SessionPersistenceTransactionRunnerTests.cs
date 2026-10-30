using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.Sessions.Coordination;

public class SessionPersistenceTransactionRunnerTests
{
    [Fact]
    public async Task DeleteSessionAsync_DeletesSessionSourceAndTrack()
    {
        using var tempDatabase = new TempDatabase("session-delete-transaction.db");
        var sessionId = Guid.NewGuid();
        var track = PersistenceTestData.CreateFullTrack();
        var source = PersistenceTestData.CreateRecordedSessionSource(sessionId);
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath);
        var connection = await context.GetInitializedConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
        var sourceRepository = new RecordedSessionSourceRepository(context);
        var runner = new SessionPersistenceTransactionRunner(context, PersistenceTestData.CreateTimeProvider());
        await connection.InsertAsync(track);
        await connection.InsertAsync(new Session(sessionId, "session", "desc", null)
        {
            FullTrack = track.Id,
            Updated = 10
        });
        await sourceRepository.PutRecordedSessionSourceAsync(source);

        await runner.DeleteSessionAsync(sessionId, track.Id, deleteFullTrack: true, cancellationToken: TestContext.Current.CancellationToken);

        var session = await connection.GetAsync<Session>(sessionId);
        var persistedTrack = await connection.GetAsync<Track>(track.Id);
        var sources = await connection.Table<RecordedSessionSource>()
            .Where(candidate => candidate.SessionId == sessionId)
            .ToListAsync();
        Assert.NotNull(session.Deleted);
        Assert.NotNull(persistedTrack.Deleted);
        Assert.Empty(sources);
    }
}
