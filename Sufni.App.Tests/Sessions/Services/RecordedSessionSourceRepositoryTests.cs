using SQLite;
using Sufni.App.Infrastructure;
using Sufni.Telemetry;

using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.Sessions.Services;

public class RecordedSessionSourceRepositoryTests
{
    [Fact]
    public async Task PutRecordedSessionSourceAsync_KeepsFirstContent_AndReturnsItsSnapshot()
    {
        using var temp = new TempDatabase();
        await using var context = PersistenceTestData.CreateConnectionContext(temp.DatabasePath);
        IRecordedSessionSourceRepository repository = new RecordedSessionSourceRepository(context);
        var original = PersistenceTestData.CreateRecordedSessionSource(Guid.NewGuid());
        var replacement = PersistenceTestData.CreateRecordedSessionSource(original.SessionId);
        replacement.SourceName = "replacement.SST";
        replacement.Payload = RecordedSessionSourcePayloadCodec.CompressImportedSst(new byte[] { 9, 8, 7 });
        replacement.SourceHash = RecordedSessionSourceHash.ComputeImportedSst([9, 8, 7]);
        await repository.PutRecordedSessionSourceAsync(original);

        var retained = await repository.PutRecordedSessionSourceAsync(replacement);

        Assert.Equal(RecordedSessionSourceSnapshot.From(original), retained);
        var stored = await repository.GetRecordedSessionSourceAsync(original.SessionId);
        Assert.Equal(original.Payload, stored!.Payload);
        Assert.Equal(original.SourceName, stored.SourceName);
        Assert.Equal(original.SourceHash, stored.SourceHash);
    }

    [Fact]
    public async Task PutRecordedSessionSourceAsync_PreservesPayloadAcrossReopen()
    {
        using var temp = new TempDatabase("source-roundtrip.db");
        var source = PersistenceTestData.CreateRecordedSessionSource(Guid.NewGuid());
        await using (var context = PersistenceTestData.CreateConnectionContext(temp.DatabasePath))
        {
            IRecordedSessionSourceRepository repository = new RecordedSessionSourceRepository(context);
            await repository.PutRecordedSessionSourceAsync(source);
        }
        await using var reopened = PersistenceTestData.CreateConnectionContext(temp.DatabasePath);
        IRecordedSessionSourceRepository reader = new RecordedSessionSourceRepository(reopened);

        var loaded = await reader.GetRecordedSessionSourceAsync(source.SessionId);

        Assert.Equal([1, 2, 3], RecordedSessionSourcePayloadCodec.DecompressImportedSst(loaded!.Payload));
        Assert.Equal(source.SourceHash, loaded.SourceHash);
    }

    [Theory]
    [InlineData(MissingRecordedSourceCase.DirectHashMismatch)]
    [InlineData(MissingRecordedSourceCase.DirectNullFingerprintHash)]
    [InlineData(MissingRecordedSourceCase.DerivationWindowSourceHashMismatch)]
    public async Task GetSessionIdsMissingRecordedSourceAsync_DoesNotRequestReplacement_WhenFingerprintDiffers(
        MissingRecordedSourceCase scenario)
    {
        using var tempDatabase = new TempDatabase("source-missing-states.db");
        var databasePath = tempDatabase.DatabasePath;

        var database = new TestPersistenceHarness(databasePath);
        await SeedMissingRecordedSourceScenarioAsync(
            database,
            scenario);

        var sourceIds = await database.GetSessionIdsMissingRecordedSourceAsync();

        Assert.Empty(sourceIds);
        Assert.Empty(await database.GetMissingRecordingSourceIdsAsync());
    }

    [Fact]
    public async Task GetMissingRecordingSourceIdsAsync_ReturnsSourceOfDerivedSession()
    {
        using var tempDatabase = new TempDatabase("source-missing-recording-source.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var sourceSessionId = Guid.NewGuid();
        await database.PutSessionAsync(new Session(Guid.NewGuid(), "derived", "desc", null, 100)
        {
            SourceSessionId = sourceSessionId,
            WindowStartSeconds = 1
        });

        var missingIds = await database.GetMissingRecordingSourceIdsAsync();

        Assert.Equal([sourceSessionId], missingIds);
    }

    [Fact]
    public async Task GetReferencedSourceSessionIdsAsync_ReturnsSourcesOfLiveDerivedSessions()
    {
        using var tempDatabase = new TempDatabase("source-referenced-ids.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var liveSourceId = Guid.NewGuid();
        var deletedSourceId = Guid.NewGuid();
        await database.PutSessionAsync(new Session(Guid.NewGuid(), "plain", "desc", null, 100));
        await database.PutSessionAsync(new Session(Guid.NewGuid(), "live", "desc", null, 101)
        {
            SourceSessionId = liveSourceId,
            WindowStartSeconds = 1
        });
        var deleted = new Session(Guid.NewGuid(), "deleted", "desc", null, 102)
        {
            SourceSessionId = deletedSourceId,
            WindowStartSeconds = 1
        };
        await database.PutSessionAsync(deleted);
        await database.DeleteAsync(deleted);

        var referencedIds = await database.GetReferencedSourceSessionIdsAsync();

        Assert.Equal([liveSourceId], referencedIds);
    }

    [Fact]
    public async Task DeleteOrphanedRecordedSessionSourcesAsync_DeletesOnlySourcesNoLiveSessionNeeds()
    {
        using var tempDatabase = new TempDatabase("source-orphans.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var ownedSourceId = Guid.NewGuid();
        var derivedFromSourceId = Guid.NewGuid();
        var deletedDerivationSourceId = Guid.NewGuid();
        var orphanSourceId = Guid.NewGuid();
        await database.PutSessionAsync(new Session(ownedSourceId, "owner", "desc", null, 100));
        await database.PutSessionAsync(new Session(Guid.NewGuid(), "derived", "desc", null, 101)
        {
            SourceSessionId = derivedFromSourceId,
            WindowStartSeconds = 1
        });
        var deletedDerived = new Session(Guid.NewGuid(), "deleted derived", "desc", null, 102)
        {
            SourceSessionId = deletedDerivationSourceId,
            WindowStartSeconds = 1
        };
        await database.PutSessionAsync(deletedDerived);
        await database.DeleteAsync(deletedDerived);
        foreach (var sourceId in new[] { ownedSourceId, derivedFromSourceId, deletedDerivationSourceId, orphanSourceId })
        {
            await database.PutRecordedSessionSourceAsync(PersistenceTestData.CreateRecordedSessionSource(sourceId));
        }

        var deleted = await database.DeleteOrphanedRecordedSessionSourcesAsync();

        Assert.Equal(new[] { deletedDerivationSourceId, orphanSourceId }.Order(), deleted.Order());
        Assert.NotNull(await database.GetRecordedSessionSourceAsync(ownedSourceId));
        Assert.NotNull(await database.GetRecordedSessionSourceAsync(derivedFromSourceId));
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

    private static async Task SeedMissingRecordedSourceScenarioAsync(
        TestPersistenceHarness database,
        MissingRecordedSourceCase scenario)
    {
        if (scenario == MissingRecordedSourceCase.DirectNullFingerprintHash)
        {
            var sessionId = Guid.NewGuid();
            var source = PersistenceTestData.CreateRecordedSessionSource(sessionId);
            await database.PutSessionWithRecordedSourceAsync(new Session(sessionId, "null hash", "desc", null, 100)
            {
                ProcessingFingerprintJson = """{"SourceHash":null}"""
            }, source);
            return;
        }

        if (scenario == MissingRecordedSourceCase.DerivationWindowSourceHashMismatch)
        {
            var matchingSourceSessionId = Guid.NewGuid();
            var matchingDerivedSessionId = Guid.NewGuid();
            var staleSourceSessionId = Guid.NewGuid();
            var staleDerivedSessionId = Guid.NewGuid();
            var matchingSource = PersistenceTestData.CreateRecordedSessionSource(matchingSourceSessionId);
            var staleSource = PersistenceTestData.CreateRecordedSessionSource(staleSourceSessionId);
            var expectedReplacementHash = RecordedSessionSourceHash.ComputeImportedSst([9, 8, 7]);

            await database.PutSessionWithRecordedSourceAsync(
                new Session(matchingSourceSessionId, "matching source", "desc", null, 100),
                matchingSource);
            await database.PutSessionWithRecordedSourceAsync(
                new Session(staleSourceSessionId, "stale source", "desc", null, 101),
                staleSource);
            await database.PutSessionAsync(new Session(matchingDerivedSessionId, "matching derived", "desc", null, 102)
            {
                ProcessingFingerprintJson = CreateFingerprintJson(
                    matchingSource.SourceHash,
                    new RecordedSessionDerivationWindow(matchingSourceSessionId, 1, 2)),
                SourceSessionId = matchingSourceSessionId,
                WindowStartSeconds = 1,
                WindowEndSeconds = 2
            });
            await database.PutSessionAsync(new Session(staleDerivedSessionId, "stale derived", "desc", null, 103)
            {
                ProcessingFingerprintJson = CreateFingerprintJson(
                    expectedReplacementHash,
                    new RecordedSessionDerivationWindow(staleSourceSessionId, 1, 2)),
                SourceSessionId = staleSourceSessionId,
                WindowStartSeconds = 1,
                WindowEndSeconds = 2
            });

            return;
        }

        var matchingSessionId = Guid.NewGuid();
        var staleSessionId = Guid.NewGuid();
        var directMatchingSource = PersistenceTestData.CreateRecordedSessionSource(matchingSessionId);
        var directStaleSource = PersistenceTestData.CreateRecordedSessionSource(staleSessionId);
        var expectedStaleHash = RecordedSessionSourceHash.ComputeImportedSst([9, 8, 7]);

        await database.PutSessionWithRecordedSourceAsync(new Session(matchingSessionId, "matching", "desc", null, 100)
        {
            ProcessingFingerprintJson = $$"""{"SourceHash":"{{directMatchingSource.SourceHash}}"}"""
        }, directMatchingSource);
        await database.PutSessionWithRecordedSourceAsync(new Session(staleSessionId, "stale", "desc", null, 101)
        {
            ProcessingFingerprintJson = $$"""{"SourceHash":"{{expectedStaleHash}}"}"""
        }, directStaleSource);


    }

    public enum MissingRecordedSourceCase
    {
        DirectHashMismatch,
        DirectNullFingerprintHash,
        DerivationWindowSourceHashMismatch
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
