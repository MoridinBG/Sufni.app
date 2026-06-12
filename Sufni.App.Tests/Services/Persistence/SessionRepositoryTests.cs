using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHosting.Database;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Services.Persistence;

public class SessionRepositoryTests
{
    [Fact]
    public async Task PutProcessedSessionAsync_PersistsSummaryMetricsAsGiven_WithoutDerivation()
    {
        using var tempDatabase = new TempDatabase("processed-session-as-given.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        // A stored track containing the session timestamp must no longer be
        // associated by the repository itself; that derivation lives in the writer.
        await database.PutAsync(new Track
        {
            Id = Guid.NewGuid(),
            Points =
            [
                new TrackPoint(90, 1, 1, 10),
                new TrackPoint(110, 2, 2, 11)
            ]
        });
        var session = new Session(sessionId, "processed", "desc", null, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65),
            DurationSeconds = 1,
            DistanceMeters = 2,
            AscentMeters = 3,
            DescentMeters = 4
        };

        var persisted = await database.SessionRepository.PutProcessedSessionAsync(session, newFullTrack: null, source: null);
        var loaded = await database.GetSessionAsync(sessionId);

        Assert.Equal(1, persisted.DurationSeconds);
        Assert.Equal(2, persisted.DistanceMeters);
        Assert.Equal(3, persisted.AscentMeters);
        Assert.Equal(4, persisted.DescentMeters);
        Assert.Null(persisted.FullTrack);
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.DurationSeconds);
        Assert.Equal(2, loaded.DistanceMeters);
        Assert.Equal(3, loaded.AscentMeters);
        Assert.Equal(4, loaded.DescentMeters);

    }

    [Fact]
    public async Task PutProcessedSessionAsync_PersistsSessionTrackSourceAndFingerprint()
    {
        using var tempDatabase = new TempDatabase("processed-session.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var track = PersistenceTestData.CreateFullTrack();
        var processedData = PersistenceTestData.CreateTelemetryBlob(65);

        var database = new TestPersistenceHarness(databasePath);
        var session = new Session(sessionId, "processed", "desc", null, 100)
        {
            ProcessedData = processedData,
            ProcessingFingerprintJson = """{"schemaVersion":1}"""
        };
        var source = PersistenceTestData.CreateRecordedSessionSource(sessionId);

        var persisted = await database.PutProcessedSessionAsync(session, track, source);

        Assert.Equal(track.Id, persisted.FullTrack);
        Assert.True(persisted.HasProcessedData);
        Assert.Equal("""{"schemaVersion":1}""", persisted.ProcessingFingerprintJson);
        Assert.Equal(processedData, await database.GetSessionRawPsstAsync(sessionId));
        Assert.NotNull(await database.GetAsync<Track>(track.Id));
        Assert.NotNull(await database.GetRecordedSessionSourceAsync(sessionId));

    }

    [Fact]
    public async Task PutSessionAsync_PreservesExistingSummaryMetrics_OnMetadataUpdate()
    {
        using var tempDatabase = new TempDatabase("session-summary-metadata-save.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        var processed = await database.PutProcessedSessionAsync(
            new Session(sessionId, "processed", "desc", null, 100)
            {
                ProcessedData = PersistenceTestData.CreateTelemetryBlob(65)
            },
            PersistenceTestData.CreateFullTrack(),
            source: null);

        await database.PutSessionAsync(new Session(sessionId, "renamed", "new desc", null, 100)
        {
            DurationSeconds = 1,
            DistanceMeters = 2,
            AscentMeters = 3,
            DescentMeters = 4
        });

        var loaded = await database.GetSessionAsync(sessionId);

        Assert.NotNull(loaded);
        Assert.Equal("renamed", loaded!.Name);
        Assert.Equal("new desc", loaded.Description);
        Assert.Equal(processed.DurationSeconds, loaded.DurationSeconds);
        Assert.Equal(processed.DistanceMeters, loaded.DistanceMeters);
        Assert.Equal(processed.AscentMeters, loaded.AscentMeters);
        Assert.Equal(processed.DescentMeters, loaded.DescentMeters);

    }

    [Fact]
    public async Task PutProcessedSessionIfUnchangedAsync_ReturnsNullAndRollsBack_WhenBaselineDoesNotMatch()
    {
        using var tempDatabase = new TempDatabase("processed-session-conflict.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var newTrack = PersistenceTestData.CreateFullTrack();

        var database = new TestPersistenceHarness(databasePath);
        var original = new Session(sessionId, "original", "desc", null, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65),
            ProcessingFingerprintJson = """{"schemaVersion":1}"""
        };
        var persisted = await database.PutProcessedSessionAsync(original, newFullTrack: null, source: null);
        var baselineUpdated = persisted.Updated;
        var newerUpdated = baselineUpdated + 10;

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Execute(
                "UPDATE session SET name=?, data=?, updated=? WHERE id=?",
                "newer",
                new byte[] { 4, 5, 6 },
                newerUpdated,
                sessionId);
        }

        var recomputed = new Session(sessionId, "recomputed", "desc", null, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(66),
            ProcessingFingerprintJson = """{"schemaVersion":2}"""
        };

        var result = await database.PutProcessedSessionIfUnchangedAsync(
            recomputed,
            newTrack,
            source: null,
            baselineUpdated);

        Assert.Null(result);
        var current = await database.GetSessionAsync(sessionId);
        Assert.NotNull(current);
        Assert.Equal("newer", current!.Name);
        Assert.Equal(newerUpdated, current.Updated);
        Assert.Equal([4, 5, 6], await database.GetSessionRawPsstAsync(sessionId));
        Assert.Null(await database.GetAsync<Track>(newTrack.Id));

    }

    [Fact]
    public async Task PutProcessedSessionAsync_RollsBackSessionTrackAndSource_WhenSourceWriteFails()
    {
        using var tempDatabase = new TempDatabase("processed-session-rollback.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var track = PersistenceTestData.CreateFullTrack();

        var database = new TestPersistenceHarness(databasePath);
        var session = new Session(sessionId, "processed", "desc", null, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65),
            ProcessingFingerprintJson = """{"schemaVersion":1}"""
        };
        var source = PersistenceTestData.CreateRecordedSessionSource(sessionId);
        source.SourceName = null!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.PutProcessedSessionAsync(session, track, source));

        Assert.Null(await database.GetSessionAsync(sessionId));
        Assert.Null(await database.GetAsync<Track>(track.Id));
        Assert.Null(await database.GetRecordedSessionSourceAsync(sessionId));

    }

    [Fact]
    public async Task GetSessionPsstAsync_ReturnsNull_WhenSessionHasNoProcessedData()
    {
        using var tempDatabase = new TempDatabase("session-psst.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        await database.PutSessionAsync(new Session(sessionId, "session", "desc", null, 100));

        var telemetryData = await database.GetSessionPsstAsync(sessionId);

        Assert.Null(telemetryData);

    }

    [Fact]
    public async Task UpdateSessionPsstAsync_WritesDataAndMetricsAsGiven_WithoutBumpingUpdated()
    {
        using var tempDatabase = new TempDatabase("session-psst-update.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        await database.PutSessionAsync(new Session(sessionId, "session", "desc", null, 100));
        var before = await database.GetSessionAsync(sessionId);

        var data = PersistenceTestData.CreateTelemetryBlob(65);
        await database.SessionRepository.UpdateSessionPsstAsync(
            sessionId,
            data,
            new SessionSummaryMetrics(65, 10, 4, 2));

        var after = await database.GetSessionAsync(sessionId);

        Assert.NotNull(after);
        Assert.Equal(65, after!.DurationSeconds);
        Assert.Equal(10, after.DistanceMeters);
        Assert.Equal(4, after.AscentMeters);
        Assert.Equal(2, after.DescentMeters);
        Assert.Equal(before!.Updated, after.Updated);
        Assert.Equal(data, await database.GetSessionRawPsstAsync(sessionId));

    }

    [Fact]
    public async Task UpdateSessionPsstAsync_Throws_WhenSessionDoesNotExist()
    {
        using var tempDatabase = new TempDatabase("session-psst-update-missing.db");
        var databasePath = tempDatabase.DatabasePath;

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        await Assert.ThrowsAsync<Exception>(() => database.SessionRepository.UpdateSessionPsstAsync(
            Guid.NewGuid(),
            [1, 2, 3],
            new SessionSummaryMetrics(null, null, null, null)));

    }

    [Fact]
    public async Task PatchSessionPsstAsync_FlipsHasProcessedData_ForChangedSessionQueries()
    {
        using var tempDatabase = new TempDatabase("session-psst-patch.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        await database.ApplyRemoteSynchronizationDataAsync(new SynchronizationData
        {
            Sessions = [new Session(sessionId, "remote", "desc", null, 100) { Updated = 10, ClientUpdated = 10 }]
        });

        var patchedPsst = PersistenceTestData.CreateTelemetryBlob(65);
        await database.PatchSessionPsstAsync(sessionId, patchedPsst);

        var changedSession = Assert.Single(await database.GetChangedAsync<Session>(0));

        Assert.True(changedSession.HasProcessedData);
        Assert.Equal(patchedPsst, await database.GetSessionRawPsstAsync(sessionId));

    }

    [Fact]
    public async Task GetChangedAsync_ForSessions_DerivesHasProcessedDataFromDataColumn()
    {
        using var tempDatabase = new TempDatabase("session-has-data-derived.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "session", "desc", null, 100)
            {
                ProcessedData = PersistenceTestData.CreateTelemetryBlob(65),
                Updated = 10
            });
            connection.Execute("UPDATE session SET has_data = 0 WHERE id = ?", sessionId);
        }

        var changedSession = Assert.Single(await database.GetChangedAsync<Session>(0));

        Assert.True(changedSession.HasProcessedData);
        Assert.Null(changedSession.ProcessedData);

    }

    [Fact]
    public async Task PutSessionAsync_ReusesSoftDeletedRow_AndPreservesExistingBinaryData()
    {
        using var tempDatabase = new TempDatabase("session-revive.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var fullTrackId = Guid.NewGuid();
        var originalPsst = new byte[] { 9, 8, 7 };
        var originalTrack = new List<TrackPoint>
        {
            new(10, 1, 1, 0),
            new(11, 2, 2, 0)
        };

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "old", "old desc", null, 50)
            {
                ProcessedData = originalPsst,
                Track = originalTrack,
                Updated = 10,
                ClientUpdated = 10,
                Deleted = 10
            });
        }

        await database.PutSessionAsync(new Session(sessionId, "new", "new desc", setupId, 1234)
        {
            FullTrack = fullTrackId,
            ProcessingFingerprintJson = """{"current":true}"""
        });

        var session = await database.GetSessionAsync(sessionId);
        var rawPsst = await database.GetSessionRawPsstAsync(sessionId);
        var sessionTrack = await database.GetSessionTrackAsync(sessionId);

        Assert.NotNull(session);
        Assert.Equal("new", session!.Name);
        Assert.Equal("new desc", session.Description);
        Assert.Equal(setupId, session.Setup);
        Assert.Equal(1234, session.Timestamp);
        Assert.Equal(fullTrackId, session.FullTrack);
        Assert.Equal("""{"current":true}""", session.ProcessingFingerprintJson);
        Assert.True(session.HasProcessedData);
        Assert.Null(session.Deleted);
        Assert.Equal(originalPsst, rawPsst);
        Assert.NotNull(sessionTrack);
        Assert.Equal(2, sessionTrack!.Count);

    }

    [Fact]
    public async Task UpdateSessionTrackAsync_WritesTrackAndMetricsAsGiven_AndBumpsUpdated()
    {
        using var tempDatabase = new TempDatabase("session-track-update.db");
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

        await database.SessionRepository.UpdateSessionTrackAsync(
            sessionId,
            [
                new TrackPoint(100, 1, 1, 0),
                new TrackPoint(101, 2, 2, 0)
            ],
            new SessionSummaryMetrics(65, 1.5, 0, 0));

        var after = await database.GetSessionAsync(sessionId);
        var track = await database.GetSessionTrackAsync(sessionId);

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.NotNull(track);
        Assert.Equal(2, track!.Count);
        Assert.Equal(65, after!.DurationSeconds);
        Assert.Equal(1.5, after.DistanceMeters);
        Assert.Equal(0, after.AscentMeters);
        Assert.Equal(0, after.DescentMeters);
        Assert.True(after!.Updated > before!.Updated);

    }

    [Fact]
    public async Task UpdateSessionTrackAsync_PreservesGpsOffset_WhenOffsetIsNotProvided()
    {
        using var tempDatabase = new TempDatabase("session-track-preserve-gps-offset.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "session", "desc", null, 100)
            {
                DurationSeconds = 65,
                GpsOffsetSeconds = 3.25,
                Updated = 1,
                ClientUpdated = 1
            });
        }

        await database.SessionRepository.UpdateSessionTrackAsync(
            sessionId,
            [
                new TrackPoint(100, 1, 1, 0),
                new TrackPoint(101, 2, 2, 0)
            ],
            new SessionSummaryMetrics(65, 1.5, 0, 0));

        var after = await database.GetSessionAsync(sessionId);

        Assert.NotNull(after);
        Assert.Equal(3.25, after!.GpsOffsetSeconds);
    }

    [Fact]
    public async Task UpdateSessionTrackAsync_Throws_WhenSessionDoesNotExist()
    {
        using var tempDatabase = new TempDatabase("session-track-update-missing.db");
        var databasePath = tempDatabase.DatabasePath;

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        await Assert.ThrowsAsync<Exception>(() => database.SessionRepository.UpdateSessionTrackAsync(
            Guid.NewGuid(),
            [new TrackPoint(100, 1, 1, 0)],
            new SessionSummaryMetrics(null, null, null, null)));

    }
}
