using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;

using Sufni.App.Extensibility.Database;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.Sessions.Coordination;

public class SessionPersistenceTransactionRunnerTests
{
    [Fact]
    public async Task DeleteSessionAsync_DeletesSessionSourceTrackAndRefreshesCascade()
    {
        using var tempDatabase = new TempDatabase("session-delete-transaction.db");
        var sessionId = Guid.NewGuid();
        var track = PersistenceTestData.CreateFullTrack();
        var source = PersistenceTestData.CreateRecordedSessionSource(sessionId);
        var migrator = CreateMigrator();
        var provider = new TestCascadeRuleProvider(new ExtensionCascadeRule(
            "test",
            ExtensionCoreEntityKind.Track,
            "session_delete_track_cascade_row",
            "track_id",
            ExtensionCascadeAction.SoftDelete));
        var refresh = new RecordingRefreshParticipant();
        var context = CreateConnectionContext(tempDatabase.DatabasePath, [migrator]);
        var cascade = new ExtensionCascadeService(context, [migrator], [provider], [refresh]);
        var connection = await context.GetInitializedConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
        var sourceRepository = new RecordedSessionSourceRepository(context);
        var runner = new SessionPersistenceTransactionRunner(context, cascade);
        await connection.InsertAsync(track);
        await connection.InsertAsync(new Session(sessionId, "session", "desc", null)
        {
            FullTrack = track.Id,
            Updated = 10
        });
        await sourceRepository.PutRecordedSessionSourceAsync(source);
        await connection.InsertAsync(new SessionDeleteTrackCascadeRow { Id = "extension", TrackId = track.Id });

        await runner.DeleteSessionAsync(sessionId, track.Id, deleteFullTrack: true, deleteSource: true, cancellationToken: TestContext.Current.CancellationToken);

        var session = await connection.GetAsync<Session>(sessionId);
        var persistedTrack = await connection.GetAsync<Track>(track.Id);
        var sources = await connection.Table<RecordedSessionSource>()
            .Where(candidate => candidate.SessionId == sessionId)
            .ToListAsync();
        var extensionRow = await connection.GetAsync<SessionDeleteTrackCascadeRow>("extension");
        Assert.NotNull(session.Deleted);
        Assert.NotNull(persistedTrack.Deleted);
        Assert.Empty(sources);
        Assert.NotNull(extensionRow.Deleted);
        Assert.Equal(extensionRow.Deleted, extensionRow.Updated);
        Assert.Equal(1, refresh.RefreshCount);
    }

    [Fact]
    public async Task DeleteSessionAsync_RollsBackSessionSourceAndTrack_WhenTrackCascadeFails()
    {
        using var tempDatabase = new TempDatabase("session-delete-rollback.db");
        var sessionId = Guid.NewGuid();
        var track = PersistenceTestData.CreateFullTrack();
        var source = PersistenceTestData.CreateRecordedSessionSource(sessionId);
        var context = CreateConnectionContext(tempDatabase.DatabasePath);
        var connection = await context.GetInitializedConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
        var sourceRepository = new RecordedSessionSourceRepository(context);
        var cascade = new ThrowingTrackCascadeService();
        var runner = new SessionPersistenceTransactionRunner(context, cascade);
        await connection.InsertAsync(track);
        await connection.InsertAsync(new Session(sessionId, "session", "desc", null)
        {
            FullTrack = track.Id,
            Updated = 10
        });
        await sourceRepository.PutRecordedSessionSourceAsync(source);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.DeleteSessionAsync(sessionId, track.Id, deleteFullTrack: true, deleteSource: true, cancellationToken: TestContext.Current.CancellationToken));

        var session = await connection.GetAsync<Session>(sessionId);
        var persistedTrack = await connection.GetAsync<Track>(track.Id);
        var sources = await connection.Table<RecordedSessionSource>()
            .Where(candidate => candidate.SessionId == sessionId)
            .ToListAsync();
        Assert.Null(session.Deleted);
        Assert.Null(persistedTrack.Deleted);
        Assert.Single(sources);
        Assert.Equal(0, cascade.RefreshCount);
    }

    private static SqliteConnectionContext CreateConnectionContext(
        string databasePath,
        IEnumerable<IExtensionDatabaseMigrator>? migrators = null) =>
        new(
            databasePath,
            createAppDirectories: false,
            migrators ?? [],
            extensionCascadeRuleProviders: [],
            extensionStateRefreshParticipantsProvider: () => []);

    private static TestExtensionMigrator CreateMigrator() =>
        new("test", targetVersion: 0, [typeof(SessionDeleteTrackCascadeRow)], []);

    [Table("session_delete_track_cascade_row")]
    private sealed class SessionDeleteTrackCascadeRow
    {
        [PrimaryKey]
        [Column("id")]
        public string Id { get; set; } = string.Empty;

        [Column("track_id")]
        public Guid TrackId { get; set; }

        [Column("deleted")]
        public long? Deleted { get; set; }

        [Column("updated")]
        public long Updated { get; set; }
    }

    private sealed class TestCascadeRuleProvider(ExtensionCascadeRule rule) : IExtensionCascadeRuleProvider
    {
        public IReadOnlyList<ExtensionCascadeRule> Rules { get; } = [rule];
    }

    private sealed class RecordingRefreshParticipant : IExtensionStateRefreshParticipant
    {
        public int RefreshCount { get; private set; }

        public Task RefreshExtensionStateAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingTrackCascadeService : IExtensionCascadeService
    {
        public int RefreshCount { get; private set; }

        public bool ApplyRulesForDeletedCoreEntityInTransaction(
            SQLiteConnection connection,
            ExtensionCoreEntityKind kind,
            Guid id)
        {
            if (kind == ExtensionCoreEntityKind.Track)
            {
                throw new InvalidOperationException("Track cascade failure");
            }

            return false;
        }

        public Task RefreshExtensionStateAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.CompletedTask;
        }

        public Task RepairOrphansAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
