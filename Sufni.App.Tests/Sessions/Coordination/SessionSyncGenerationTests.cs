using NSubstitute;
using SQLite;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Store;
using Sufni.App.Sessions.Services;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.Sessions.Coordination;

public class SessionSyncGenerationTests
{
    [Theory]
    [InlineData("other", true, SessionDataApplyResult.Superseded)]
    [InlineData("target", false, SessionDataApplyResult.InvalidPayload)]
    [InlineData("target", true, SessionDataApplyResult.Applied)]
    public async Task ApplyUploadedAsync_KeepsRequestUntilMatchingValidBytesCommit(
        string fingerprint, bool valid, SessionDataApplyResult expectedResult)
    {
        using var temp = new TempDatabase();
        var database = new TestPersistenceHarness(temp.DatabasePath);
        var session = new Session(Guid.NewGuid(), "ride", "notes", null)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65), ProcessingFingerprintJson = "held"
        };
        await database.PutProcessedSessionAsync(session, null, null);
        var connection = await database.GetInitializedConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO session_blob_swap_request (session_id, target_fingerprint) VALUES (?, ?)", session.Id, "target");
        var incoming = valid ? PersistenceTestData.CreateTelemetryBlob(80) : new byte[] { 1, 2, 3 };

        var result = await database.SessionSyncApplier.ApplyUploadedAsync(session.Id, new SessionBlobPayload(fingerprint, incoming));

        Assert.Equal(expectedResult, result);
        if (result == SessionDataApplyResult.Applied)
        {
            Assert.Null(await database.GetSessionBlobSwapRequestAsync(session.Id));
            Assert.Equal(incoming, await database.GetSessionRawPsstAsync(session.Id));
            Assert.Equal("target", database.PublishedSessions.Get(session.Id)!.ProcessingFingerprintJson);
        }
        else
        {
            Assert.Equal("target", (await database.GetSessionBlobSwapRequestAsync(session.Id))!.TargetFingerprint);
            Assert.Equal(session.ProcessedData, await database.GetSessionRawPsstAsync(session.Id));
            Assert.Null(database.PublishedSessions.Get(session.Id));
        }
    }

    [Fact]
    public async Task ApplyDownloadedAsync_DoesNotReplaceGenerationChangedDuringTransfer()
    {
        using var temp = new TempDatabase();
        var database = new TestPersistenceHarness(temp.DatabasePath);
        var session = new Session(Guid.NewGuid(), "ride", "notes", null)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65), ProcessingFingerprintJson = "held"
        };
        await database.PutProcessedSessionAsync(session, null, null);
        var captured = SessionSnapshot.From((await database.GetSessionAsync(session.Id))!);
        var newer = PersistenceTestData.CreateTelemetryBlob(80);
        Assert.True(await database.SessionRepository.UpdateSessionPsstAsync(session.Id, newer, "held",
            new SessionSummaryMetrics(80, null, null, null), SessionProcessedWriteGuard.From(captured)));

        var result = await database.SessionSyncApplier.ApplyDownloadedAsync(captured,
            new SessionBlobPayload("target", PersistenceTestData.CreateTelemetryBlob(90)), "target");

        Assert.Equal(SessionDataApplyResult.Superseded, result);
        Assert.Equal(newer, await database.GetSessionRawPsstAsync(session.Id));
        Assert.Null(database.PublishedSessions.Get(session.Id));
    }

    [Fact]
    public async Task PatchSessionPsstAsync_UpdatesDuration_AndPreservesGpsMetrics_WithoutTrackPoints()
    {
        using var tempDatabase = new TempDatabase("writer-psst-patch-metrics.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        await database.PutSessionAsync(new Session(sessionId, "session", "desc", null, 100)
        {
            DistanceMeters = 10,
            AscentMeters = 4,
            DescentMeters = 2
        });

        await database.PatchSessionPsstAsync(sessionId, PersistenceTestData.CreateTelemetryBlob(65));

        var session = await database.GetSessionAsync(sessionId);

        Assert.NotNull(session);
        Assert.Equal(65, session!.DurationSeconds);
        Assert.Equal(10, session.DistanceMeters);
        Assert.Equal(4, session.AscentMeters);
        Assert.Equal(2, session.DescentMeters);

    }

    [Fact]
    public async Task PatchSessionPsstAsync_RejectsInvalidBlob_WithoutChangingExistingData()
    {
        using var tempDatabase = new TempDatabase("writer-psst-invalid-patch.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var originalPsst = PersistenceTestData.CreateTelemetryBlob(65);

        var database = new TestPersistenceHarness(databasePath);
        await database.PutProcessedSessionAsync(new Session(sessionId, "session", "desc", null, 100)
        {
            ProcessedData = originalPsst
        }, newFullTrack: null, source: null);

        Assert.Equal(SessionDataApplyResult.InvalidPayload, await database.PatchSessionPsstAsync(sessionId, [1, 2, 3]));

        var session = await database.GetSessionAsync(sessionId);

        Assert.NotNull(session);
        Assert.True(session!.HasProcessedData);
        Assert.Equal(65, session.DurationSeconds);
        Assert.Equal(originalPsst, await database.GetSessionRawPsstAsync(sessionId));

    }

    [Fact]
    public async Task PatchSessionPsstAsync_RejectsMismatchedFingerprint_WithoutChangingExistingData()
    {
        using var tempDatabase = new TempDatabase("writer-psst-fingerprint-reject.db");
        var sessionId = Guid.NewGuid();
        var heldBlob = PersistenceTestData.CreateTelemetryBlob(65);
        const string heldFingerprint = """{"schemaVersion":3,"velocityFilterWindowMilliseconds":25}""";
        var incomingBlob = PersistenceTestData.CreateTelemetryBlob(80);
        const string incomingFingerprint = """{"schemaVersion":3,"velocityFilterWindowMilliseconds":100}""";

        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        await database.PutProcessedSessionAsync(new Session(sessionId, "session", "desc", null, 100)
        {
            ProcessedData = heldBlob,
            ProcessingFingerprintJson = heldFingerprint
        }, newFullTrack: null, source: null);

        // The bytes are valid, so the rejection is purely the fingerprint integrity
        // check (the hub asked for the BLOB matching its stored fingerprint). The held
        // BLOB and its fingerprint are left intact for a later retry.
        Assert.Equal(SessionDataApplyResult.InvalidPayload,
            await database.PatchSessionPsstAsync(sessionId, incomingBlob, incomingFingerprint));

        var session = await database.GetSessionAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal(heldBlob, await database.GetSessionRawPsstAsync(sessionId));
        Assert.Equal(heldFingerprint, session!.ProcessingFingerprintJson);
    }

    [Fact]
    public async Task ApplyDownloadedAsync_ValidatesAndPublishesCompleteGeneration_WithoutChangingAuthoredFields()
    {
        using var temp = new TempDatabase();
        var database = new TestPersistenceHarness(temp.DatabasePath);
        var session = new Session(Guid.NewGuid(), "ride", "notes", null, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65),
            ProcessingFingerprintJson = "held",
            GpsOffsetSeconds = 7
        };
        await database.PutProcessedSessionAsync(session, null, null);
        var before = (await database.GetSessionAsync(session.Id))!;
        var data = PersistenceTestData.CreateTelemetryBlob(80);
        var generation = new SessionProcessedGeneration(80, 20, 8, 3, Guid.NewGuid(), [new TrackPoint(100, 1, 2, 3)]);

        var result = await database.SessionSyncApplier.ApplyDownloadedAsync(SessionSnapshot.From(before),
            new SessionBlobPayload("target", data), "target", generation);

        Assert.Equal(SessionDataApplyResult.Applied, result);
        var saved = (await database.GetSessionAsync(session.Id))!;
        Assert.Equal(before.Updated, saved.Updated);
        Assert.Equal("notes", saved.Description);
        Assert.Equal(7, saved.GpsOffsetSeconds);
        Assert.Equal(data, await database.GetSessionRawPsstAsync(session.Id));
        Assert.Equal(generation.DurationSeconds, saved.DurationSeconds);
        Assert.Equal(generation.DistanceMeters, saved.DistanceMeters);
        Assert.Equal(generation.FullTrackId, saved.FullTrack);
        Assert.Equal(SessionSnapshot.From(saved), database.PublishedSessions.Get(session.Id));
    }

    [Fact]
    public async Task SwapSessionPsstAsync_OverwritesHeldBlobAndFingerprint_WhenFingerprintDiffers()
    {
        using var tempDatabase = new TempDatabase("writer-psst-swap.db");
        var sessionId = Guid.NewGuid();
        var heldBlob = PersistenceTestData.CreateTelemetryBlob(65);
        const string heldFingerprint = """{"schemaVersion":3,"velocityFilterWindowMilliseconds":25}""";
        var incomingBlob = PersistenceTestData.CreateTelemetryBlob(80);
        const string incomingFingerprint = """{"schemaVersion":3,"velocityFilterWindowMilliseconds":100}""";

        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        await database.PutProcessedSessionAsync(new Session(sessionId, "session", "desc", null, 100)
        {
            ProcessedData = heldBlob,
            ProcessingFingerprintJson = heldFingerprint
        }, newFullTrack: null, source: null);

        // The caller already matched the downloaded fingerprint to its target, so the
        // swap replaces the held BLOB and its fingerprint coherently even though they
        // differ from what the row currently advertises.
        await database.SwapSessionPsstAsync(sessionId, incomingBlob, incomingFingerprint);

        var session = await database.GetSessionAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal(incomingBlob, await database.GetSessionRawPsstAsync(sessionId));
        Assert.Equal(incomingFingerprint, session!.ProcessingFingerprintJson);
        Assert.Equal(80, session.DurationSeconds);
    }

}
