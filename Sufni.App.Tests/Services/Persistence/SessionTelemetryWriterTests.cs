using SQLite;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Models;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Services.Persistence;

public class SessionTelemetryWriterTests
{
    [Fact]
    public async Task PutProcessedSessionAsync_ComputesMetricsFromSessionTrack()
    {
        using var tempDatabase = new TempDatabase("writer-session-track-metrics.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        var session = new Session(sessionId, "processed", "desc", null, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65),
            Track =
            [
                new TrackPoint(100, 0, 0, 10),
                new TrackPoint(101, 3, 4, 14),
                new TrackPoint(102, 6, 8, 10)
            ]
        };

        var persisted = await database.PutProcessedSessionAsync(session, newFullTrack: null, source: null);

        Assert.Equal(65, persisted.DurationSeconds);
        Assert.InRange(persisted.DistanceMeters!.Value, 9.98, 10.0);
        Assert.Equal(4, persisted.AscentMeters);
        Assert.Equal(4, persisted.DescentMeters);

    }

    [Fact]
    public async Task PutProcessedSessionAsync_ComputesMetricsFromNewFullTrackPoints()
    {
        using var tempDatabase = new TempDatabase("writer-full-track-metrics.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var track = new Track
        {
            Id = Guid.NewGuid(),
            Points =
            [
                new TrackPoint(100, 0, 0, 10),
                new TrackPoint(101, 3, 4, 14),
                new TrackPoint(102, 6, 8, 10)
            ]
        };

        var database = new TestPersistenceHarness(databasePath);
        var session = new Session(sessionId, "processed", "desc", null, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65)
        };

        var persisted = await database.PutProcessedSessionAsync(session, track, source: null);
        var loaded = await database.GetSessionAsync(sessionId);

        Assert.Equal(65, persisted.DurationSeconds);
        Assert.InRange(persisted.DistanceMeters!.Value, 9.98, 10.0);
        Assert.Equal(4, persisted.AscentMeters);
        Assert.Equal(4, persisted.DescentMeters);
        Assert.NotNull(loaded);
        Assert.Equal(65, loaded!.DurationSeconds);
        Assert.InRange(loaded.DistanceMeters!.Value, 9.98, 10.0);
        Assert.Equal(4, loaded.AscentMeters);
        Assert.Equal(4, loaded.DescentMeters);

    }

    [Fact]
    public async Task PutProcessedSessionAsync_ComputesMetricsFromStoredFullTrack_UsingSessionWindow()
    {
        using var tempDatabase = new TempDatabase("writer-stored-track-window-metrics.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var fullTrack = new Track
        {
            Id = Guid.NewGuid(),
            Points =
            [
                new TrackPoint(90, -3, -4, 8),
                new TrackPoint(100, 0, 0, 10),
                new TrackPoint(130, 3, 4, 14),
                new TrackPoint(160, 6, 8, 10),
                new TrackPoint(200, 9, 12, 12)
            ]
        };

        var database = new TestPersistenceHarness(databasePath);
        await database.PutAsync(fullTrack);
        var session = new Session(sessionId, "processed", "desc", null, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65),
            FullTrack = fullTrack.Id
        };

        var persisted = await database.PutProcessedSessionAsync(session, newFullTrack: null, source: null);

        var expectedPoints = fullTrack.GenerateSessionTrack(100, 165);
        var expected = SessionSummaryMetricsCalculator.Calculate(65, expectedPoints);

        Assert.NotEmpty(expectedPoints);
        Assert.Equal(expected.DurationSeconds, persisted.DurationSeconds);
        Assert.Equal(expected.DistanceMeters, persisted.DistanceMeters);
        Assert.Equal(expected.AscentMeters, persisted.AscentMeters);
        Assert.Equal(expected.DescentMeters, persisted.DescentMeters);

    }

    [Fact]
    public async Task PutProcessedSessionAsync_AssociatesExistingTrack_WhenSessionHasNoGeneratedTrack()
    {
        using var tempDatabase = new TempDatabase("writer-existing-track.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var trackId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        await database.PutAsync(new Track
        {
            Id = trackId,
            Points =
            [
                new TrackPoint(90, 1, 1, 10),
                new TrackPoint(110, 2, 2, 11)
            ]
        });
        var session = new Session(sessionId, "processed", "desc", null, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65),
            ProcessingFingerprintJson = """{"schemaVersion":1}"""
        };

        var persisted = await database.PutProcessedSessionAsync(session, newFullTrack: null, source: null);

        Assert.Equal(trackId, persisted.FullTrack);
        Assert.Single(await database.GetAllAsync<Track>());

    }

    [Fact]
    public async Task PutProcessedSessionIfUnchangedAsync_ReturnsNull_WhenBaselineDoesNotMatch()
    {
        using var tempDatabase = new TempDatabase("writer-conflict.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        var persisted = await database.PutProcessedSessionAsync(
            new Session(sessionId, "original", "desc", null, 100)
            {
                ProcessedData = PersistenceTestData.CreateTelemetryBlob(65)
            },
            newFullTrack: null,
            source: null);

        var result = await database.PutProcessedSessionIfUnchangedAsync(
            new Session(sessionId, "recomputed", "desc", null, 100)
            {
                ProcessedData = PersistenceTestData.CreateTelemetryBlob(66)
            },
            newFullTrack: null,
            source: null,
            persisted.Updated - 1);

        Assert.Null(result);
        var current = await database.GetSessionAsync(sessionId);
        Assert.NotNull(current);
        Assert.Equal("original", current!.Name);

    }

    [Fact]
    public async Task PutProcessedSessionAsync_RejectsInvalidBlob_WithoutPersistingSessionTrackOrSource()
    {
        using var tempDatabase = new TempDatabase("writer-invalid-full-write.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        var track = PersistenceTestData.CreateFullTrack();
        var source = PersistenceTestData.CreateRecordedSessionSource(sessionId);
        var session = new Session(sessionId, "processed", "desc", null, 100)
        {
            ProcessedData = [1, 2, 3],
            ProcessingFingerprintJson = """{"schemaVersion":1}"""
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => database.PutProcessedSessionAsync(session, track, source));

        Assert.Null(await database.GetSessionAsync(sessionId));
        Assert.Null(await database.GetAsync<Track>(track.Id));
        Assert.Null(await database.GetRecordedSessionSourceAsync(sessionId));
        Assert.Null(session.FullTrack);

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

        await Assert.ThrowsAsync<InvalidDataException>(() => database.PatchSessionPsstAsync(sessionId, [1, 2, 3]));

        var session = await database.GetSessionAsync(sessionId);

        Assert.NotNull(session);
        Assert.True(session!.HasProcessedData);
        Assert.Equal(65, session.DurationSeconds);
        Assert.Equal(originalPsst, await database.GetSessionRawPsstAsync(sessionId));

    }

    [Fact]
    public async Task PatchSessionTrackAsync_RecomputesMetrics_AndBumpsSessionUpdated()
    {
        using var tempDatabase = new TempDatabase("writer-track-patch.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "session", "desc", null, 100)
            {
                DurationSeconds = 65,
                Updated = 1,
                ClientUpdated = 1
            });
        }

        var before = await database.GetSessionAsync(sessionId);

        await database.PatchSessionTrackAsync(sessionId,
        [
            new TrackPoint(100, 1, 1, 0),
            new TrackPoint(101, 2, 2, 0)
        ]);

        var after = await database.GetSessionAsync(sessionId);
        var track = await database.GetSessionTrackAsync(sessionId);

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.NotNull(track);
        Assert.Equal(2, track!.Count);
        Assert.Equal(65, after!.DurationSeconds);
        Assert.InRange(after.DistanceMeters!.Value, 1.41, 1.42);
        Assert.Equal(0, after.AscentMeters);
        Assert.Equal(0, after.DescentMeters);
        Assert.True(after!.Updated > before!.Updated);

    }
}
