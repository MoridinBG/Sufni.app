using NSubstitute;
using SQLite;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.Sessions.Processing.Services;

public class SessionTelemetryWriterTests
{
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

        await Assert.ThrowsAsync<InvalidDataException>(() => database.PatchSessionPsstAsync(sessionId, [1, 2, 3]));

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
        await Assert.ThrowsAsync<InvalidDataException>(
            () => database.PatchSessionPsstAsync(sessionId, incomingBlob, incomingFingerprint));

        var session = await database.GetSessionAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal(heldBlob, await database.GetSessionRawPsstAsync(sessionId));
        Assert.Equal(heldFingerprint, session!.ProcessingFingerprintJson);
    }

    [Fact]
    public async Task SwapSessionPsstAsync_WithTargetGeneration_ValidatesBytesAndPersistsGenerationAsGiven()
    {
        var sessionId = Guid.NewGuid();
        var data = PersistenceTestData.CreateTelemetryBlob(80);
        var generation = new SessionProcessedGeneration(
            DurationSeconds: 80,
            DistanceMeters: 20,
            AscentMeters: 8,
            DescentMeters: 3,
            FullTrackId: Guid.NewGuid(),
            Track: [new TrackPoint(100, 1, 2, 3)]);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        telemetryProcessor.Map(data, TestTelemetryData.CreateMinimal(duration: 80));
        var writer = new SessionTelemetryWriter(
            sessionRepository,
            telemetryProcessor);

        await writer.SwapSessionPsstAsync(sessionId, data, "target", generation);

        Assert.Equal(1, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(1).UpdateSessionProcessedGenerationAsync(
            sessionId,
            data,
            "target",
            generation);
        await sessionRepository.DidNotReceive().GetSessionAsync(Arg.Any<Guid>());
        await sessionRepository.DidNotReceive().GetSessionTrackAsync(Arg.Any<Guid>());
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
