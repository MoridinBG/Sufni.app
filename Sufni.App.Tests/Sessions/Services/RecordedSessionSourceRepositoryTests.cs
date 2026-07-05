using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Infrastructure;
using Sufni.Telemetry;

using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.Sessions.Services;

public class RecordedSessionSourceRepositoryTests
{
    [Fact]
    public async Task RecordedSessionSourceCrud_RoundTripsSourceAndMissingSourceIds()
    {
        using var tempDatabase = new TempDatabase("source-crud.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var missingSessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        await database.PutSessionAsync(new Session(sessionId, "with source", "desc", null, 100));
        await database.PutSessionAsync(new Session(missingSessionId, "missing source", "desc", null, 101));

        var source = PersistenceTestData.CreateRecordedSessionSource(sessionId);

        await database.PutRecordedSessionSourceAsync(source);

        var loaded = await database.GetRecordedSessionSourceAsync(sessionId);
        var allSources = await database.GetRecordedSessionSourcesAsync();
        var allSnapshots = await database.GetRecordedSessionSourceSnapshotsAsync();
        var snapshot = await database.GetRecordedSessionSourceSnapshotAsync(sessionId);
        var missingSourceIds = await database.GetSessionIdsMissingRecordedSourceAsync();

        Assert.NotNull(loaded);
        Assert.Equal(sessionId, loaded!.SessionId);
        Assert.Equal(RecordedSessionSourceKind.ImportedSst, loaded.SourceKind);
        Assert.Equal("source.SST", loaded.SourceName);
        Assert.Equal(1, loaded.SchemaVersion);
        Assert.Equal(source.SourceHash, loaded.SourceHash);
        Assert.Equal([1, 2, 3], loaded.Payload);
        Assert.Single(allSources);
        Assert.Equal(RecordedSessionSourceSnapshot.From(source), Assert.Single(allSnapshots));
        Assert.Equal(RecordedSessionSourceSnapshot.From(source), snapshot);
        Assert.DoesNotContain(sessionId, missingSourceIds);
        Assert.Contains(missingSessionId, missingSourceIds);

        await database.DeleteRecordedSessionSourceAsync(sessionId);

        Assert.Null(await database.GetRecordedSessionSourceAsync(sessionId));
        Assert.Null(await database.GetRecordedSessionSourceSnapshotAsync(sessionId));
        Assert.Contains(sessionId, await database.GetSessionIdsMissingRecordedSourceAsync());

    }

    [Fact]
    public async Task SnapshotProjection_DoesNotRequirePayloadLoad()
    {
        using var tempDatabase = new TempDatabase("source-snapshot-projection.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        await database.PutSessionAsync(new Session(sessionId, "session", "desc", null, 100));
        var source = PersistenceTestData.CreateRecordedSessionSource(sessionId);
        await database.PutRecordedSessionSourceAsync(source);

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Execute("UPDATE session_recording_source SET payload = ? WHERE session_id = ?", new byte[] { 9, 8, 7, 6 }, sessionId);
        }

        var snapshot = await database.GetRecordedSessionSourceSnapshotAsync(sessionId);
        var fullSource = await database.GetRecordedSessionSourceAsync(sessionId);

        Assert.Equal(new RecordedSessionSourceSnapshot(
            source.SessionId,
            source.SourceKind,
            source.SourceName,
            source.SchemaVersion,
            source.SourceHash), snapshot);
        Assert.NotNull(fullSource);
        Assert.Equal([9, 8, 7, 6], fullSource!.Payload);
    }

    [Fact]
    public async Task GetSessionIdsMissingRecordedSourceAsync_IncludesSourceHashMismatches()
    {
        using var tempDatabase = new TempDatabase("source-mismatch.db");
        var databasePath = tempDatabase.DatabasePath;
        var matchingSessionId = Guid.NewGuid();
        var staleSessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        var matchingSource = PersistenceTestData.CreateRecordedSessionSource(matchingSessionId);
        var staleSource = PersistenceTestData.CreateRecordedSessionSource(staleSessionId);
        var expectedStaleHash = RecordedSessionSourceHash.Compute(
            RecordedSessionSourceKind.ImportedSst,
            "replacement.SST",
            1,
            [9, 8, 7]);

        await database.PutSessionAsync(new Session(matchingSessionId, "matching", "desc", null, 100)
        {
            ProcessingFingerprintJson = $$"""{"SourceHash":"{{matchingSource.SourceHash}}"}"""
        });
        await database.PutSessionAsync(new Session(staleSessionId, "stale", "desc", null, 101)
        {
            ProcessingFingerprintJson = $$"""{"SourceHash":"{{expectedStaleHash}}"}"""
        });
        await database.PutRecordedSessionSourceAsync(matchingSource);
        await database.PutRecordedSessionSourceAsync(staleSource);

        var sourceIds = await database.GetSessionIdsMissingRecordedSourceAsync();

        Assert.DoesNotContain(matchingSessionId, sourceIds);
        Assert.Contains(staleSessionId, sourceIds);

    }

    [Fact]
    public async Task DeleteOrphanedRecordedSessionSourcesAsync_KeepsMoreThanSqliteParameterLimitRetainedSources()
    {
        using var tempDatabase = new TempDatabase("source-retained-over-parameter-limit.db");
        var databasePath = tempDatabase.DatabasePath;
        var liveSessionId = Guid.NewGuid();
        var retainedSessionIds = Enumerable.Range(0, 1_005).Select(_ => Guid.NewGuid()).ToArray();
        var unretainedOrphanSessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        await database.PutSessionAsync(new Session(liveSessionId, "live", "desc", null, 100));
        await database.PutRecordedSessionSourceAsync(PersistenceTestData.CreateRecordedSessionSource(liveSessionId));
        foreach (var retainedSessionId in retainedSessionIds)
        {
            await database.PutRecordedSessionSourceAsync(PersistenceTestData.CreateRecordedSessionSource(retainedSessionId));
        }

        await database.PutRecordedSessionSourceAsync(
            PersistenceTestData.CreateRecordedSessionSource(unretainedOrphanSessionId));

        var deleted = await database.DeleteOrphanedRecordedSessionSourcesAsync(retainedSessionIds);

        Assert.Equal([unretainedOrphanSessionId], deleted);
        Assert.NotNull(await database.GetRecordedSessionSourceAsync(liveSessionId));
        Assert.NotNull(await database.GetRecordedSessionSourceAsync(retainedSessionIds[0]));
        Assert.NotNull(await database.GetRecordedSessionSourceAsync(retainedSessionIds[^1]));
        Assert.Null(await database.GetRecordedSessionSourceAsync(unretainedOrphanSessionId));
        Assert.Equal(retainedSessionIds.Length + 1, (await database.GetRecordedSessionSourcesAsync()).Count);
    }

    [Fact]
    public async Task GetSessionIdsMissingRecordedSourceAsync_IncludesNullFingerprintSourceHash()
    {
        using var tempDatabase = new TempDatabase("source-null-fingerprint-hash.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        var source = PersistenceTestData.CreateRecordedSessionSource(sessionId);
        await database.PutSessionAsync(new Session(sessionId, "null hash", "desc", null, 100)
        {
            ProcessingFingerprintJson = """{"SourceHash":null}"""
        });
        await database.PutRecordedSessionSourceAsync(source);

        var sourceIds = await database.GetSessionIdsMissingRecordedSourceAsync();

        Assert.Contains(sessionId, sourceIds);
    }

    [Fact]
    public async Task GetSessionIdsMissingRecordedSourceAsync_UsesDerivationWindowSource()
    {
        using var tempDatabase = new TempDatabase("source-derived-parent.db");
        var databasePath = tempDatabase.DatabasePath;
        var matchingSourceSessionId = Guid.NewGuid();
        var matchingDerivedSessionId = Guid.NewGuid();
        var staleSourceSessionId = Guid.NewGuid();
        var staleDerivedSessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        var matchingSource = PersistenceTestData.CreateRecordedSessionSource(matchingSourceSessionId);
        var staleSource = PersistenceTestData.CreateRecordedSessionSource(staleSourceSessionId);
        var expectedReplacementHash = RecordedSessionSourceHash.Compute(
            RecordedSessionSourceKind.ImportedSst,
            "replacement.SST",
            1,
            [9, 8, 7]);

        await database.PutSessionAsync(new Session(matchingSourceSessionId, "matching source", "desc", null, 100));
        await database.PutSessionAsync(new Session(staleSourceSessionId, "stale source", "desc", null, 101));
        await database.PutSessionAsync(new Session(matchingDerivedSessionId, "matching derived", "desc", null, 102)
        {
            ProcessingFingerprintJson = CreateFingerprintJson(
                matchingSource.SourceHash,
                new RecordedSessionDerivationWindow(matchingSourceSessionId, 1, 2))
        });
        await database.PutSessionAsync(new Session(staleDerivedSessionId, "stale derived", "desc", null, 103)
        {
            ProcessingFingerprintJson = CreateFingerprintJson(
                expectedReplacementHash,
                new RecordedSessionDerivationWindow(staleSourceSessionId, 1, 2))
        });
        await database.PutRecordedSessionSourceAsync(matchingSource);
        await database.PutRecordedSessionSourceAsync(staleSource);

        var sourceIds = await database.GetSessionIdsMissingRecordedSourceAsync();

        Assert.DoesNotContain(matchingDerivedSessionId, sourceIds);
        Assert.Contains(staleDerivedSessionId, sourceIds);
    }

    [Fact]
    public async Task PutRecordedSessionSourceAsync_RejectsHashMismatch()
    {
        using var tempDatabase = new TempDatabase("source-invalid-hash.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        await database.PutSessionAsync(new Session(sessionId, "session", "desc", null, 100));
        var source = PersistenceTestData.CreateRecordedSessionSource(sessionId);
        source.SourceHash = "not-the-payload-hash";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.PutRecordedSessionSourceAsync(source));

        Assert.Null(await database.GetRecordedSessionSourceAsync(sessionId));

    }

    private static string CreateFingerprintJson(string sourceHash, RecordedSessionDerivationWindow window)
    {
        var fingerprint = new ProcessingFingerprint(
            SchemaVersion: 1,
            ProcessingVersion: 1,
            SetupId: Guid.NewGuid(),
            BikeId: Guid.NewGuid(),
            TrackProjectionVersion: 1,
            DependencyHash: "dependency",
            SourceHash: sourceHash,
            DerivationWindow: window);

        return AppJson.Serialize(fingerprint);
    }
}
