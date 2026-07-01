using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;

using Sufni.App.Sessions.Models;
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
        var missingSourceIds = await database.GetSessionIdsMissingRecordedSourceAsync();

        Assert.NotNull(loaded);
        Assert.Equal(sessionId, loaded!.SessionId);
        Assert.Equal(RecordedSessionSourceKind.ImportedSst, loaded.SourceKind);
        Assert.Equal("source.SST", loaded.SourceName);
        Assert.Equal(1, loaded.SchemaVersion);
        Assert.Equal(source.SourceHash, loaded.SourceHash);
        Assert.Equal([1, 2, 3], loaded.Payload);
        Assert.Single(allSources);
        Assert.DoesNotContain(sessionId, missingSourceIds);
        Assert.Contains(missingSessionId, missingSourceIds);

        await database.DeleteRecordedSessionSourceAsync(sessionId);

        Assert.Null(await database.GetRecordedSessionSourceAsync(sessionId));
        Assert.Contains(sessionId, await database.GetSessionIdsMissingRecordedSourceAsync());

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
}
