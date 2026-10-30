using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.Sessions.Coordination;

public class SessionPersistenceTransactionRunnerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DeleteSessionAsync_RetainsTrackOnlyWhenAnotherLiveSessionUsesIt(bool shared, bool otherDeleted)
    {
        using var tempDatabase = new TempDatabase("session-delete-transaction.db");
        var sessionId = Guid.NewGuid();
        var track = PersistenceTestData.CreateFullTrack();
        var source = PersistenceTestData.CreateRecordedSessionSource(sessionId);
        await using var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath);
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
        if (shared)
            await connection.InsertAsync(new Session(Guid.NewGuid(), "other", "", null)
            {
                FullTrack = track.Id,
                Deleted = otherDeleted ? 1 : null
            });

        await runner.DeleteSessionAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);

        var session = await connection.GetAsync<Session>(sessionId);
        var persistedTrack = await connection.GetAsync<Track>(track.Id);
        var sources = await connection.Table<RecordedSessionSource>()
            .Where(candidate => candidate.SessionId == sessionId)
            .ToListAsync();
        Assert.NotNull(session.Deleted);
        if (shared && !otherDeleted) Assert.Null(persistedTrack.Deleted);
        else Assert.NotNull(persistedTrack.Deleted);
        Assert.Empty(sources);
    }
}
