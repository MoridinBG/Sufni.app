using SQLite;
using Sufni.App.Acquisition.Models;
using Sufni.Telemetry;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.Sessions.Services;

public class SessionRepositoryTests
{
    [Fact]
    public async Task GetImportCheckpointAsync_AfterReopening_FindsLegacyContentWithoutLoadingPayloads()
    {
        using var temp = new TempDatabase();
        using var file = new TelemetryFileSource("original.SST", [1, 2, 3]);
        var source = RecordedSessionSourceFactory.CreateImportedSst(file);
        source.SessionId = Guid.NewGuid();
        var session = new Session(source.SessionId, "original", "notes", null, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65)
        };
        await using (var database = new TestPersistenceHarness(temp.DatabasePath))
            await database.SessionRepository.ImportProcessedSessionAsync(session, null, source);
        await using var reopened = new TestPersistenceHarness(temp.DatabasePath);

        var checkpoint = await reopened.SessionRepository.GetImportCheckpointAsync(source.SourceHash);

        Assert.NotNull(checkpoint);
        Assert.Equal(source.SessionId, checkpoint.Source.SessionId);
        Assert.Equal("original.SST", checkpoint.Source.SourceName);
        Assert.Equal("original", checkpoint.Session!.Name);
        Assert.Null(checkpoint.Session.ProcessedData);
        Assert.Null(await reopened.SessionRepository.GetImportCheckpointAsync("unseen"));
    }

    [Fact]
    public async Task ImportProcessedSessionAsync_AfterPayloadPurge_RestoresAboveLedger()
    {
        using var temp = new TempDatabase();
        var clock = PersistenceTestData.CreateTimeProvider();
        using var file = new TelemetryFileSource("ride.SST", [1, 2, 3]);
        var source = RecordedSessionSourceFactory.CreateImportedSst(file);
        var session = new Session(source.SessionId, "ride", "notes", null, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65), DurationSeconds = 65
        };
        await using (var database = new TestPersistenceHarness(temp.DatabasePath, clock))
        {
            await database.SessionRepository.ImportProcessedSessionAsync(session, null, source);
            await database.SessionTransactions.DeleteSessionAsync(session.Id, TestContext.Current.CancellationToken);
        }
        clock.Advance(TimeSpan.FromDays(2));
        await using var reopened = new TestPersistenceHarness(temp.DatabasePath, clock);
        var deletions = await reopened.GetSynchronizationDataAsync(0, long.MaxValue);
        Assert.Empty(deletions.Sessions);
        var ledger = Assert.Single(deletions.Deletions);

        var result = await reopened.SessionRepository.ImportProcessedSessionAsync(session, null, source);

        Assert.IsType<SessionImportCommitResult.Inserted>(result);
        Assert.Equal(ledger.Version + 1, result.Session!.Version);
        Assert.Equal(result.Session.Version, (await reopened.GetSessionAsync(session.Id))!.Version);
        Assert.Equal(session.ProcessedData, await reopened.GetSessionRawPsstAsync(session.Id));
        Assert.Equal(source.Payload, (await reopened.GetRecordedSessionSourceAsync(session.Id))!.Payload);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportProcessedSessionAsync_SharesContentTrack_AndRestoresItIfDeleted(bool deleted)
    {
        using var temp = new TempDatabase();
        var clock = PersistenceTestData.CreateTimeProvider();
        await using var database = new TestPersistenceHarness(temp.DatabasePath, clock);
        var track = new Track(PersistenceTestData.CreateFullTrack().Points);
        await database.PutAsync(track);
        var before = (await database.GetAsync<Track>(track.Id))!;
        if (deleted) await database.DeleteAsync(track);
        clock.Advance(TimeSpan.FromSeconds(1));
        using var file = new TelemetryFileSource("ride.SST", [1, 2, 3]);
        var source = RecordedSessionSourceFactory.CreateImportedSst(file);
        var session = new Session(source.SessionId, "ride", "notes", null, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65)
        };

        var result = await database.SessionRepository.ImportProcessedSessionAsync(
            session, new Track(PersistenceTestData.CreateFullTrack().Points), source);

        Assert.IsType<SessionImportCommitResult.Inserted>(result);

        var retained = Assert.Single(await database.GetAllAsync<Track>());
        Assert.Equal(track.Id, retained.Id);
        Assert.Equal(before.PointsRevision, retained.PointsRevision);
        Assert.Equal(deleted ? clock.GetUtcNow().ToUnixTimeSeconds() : before.Updated, retained.Updated);
    }

    [Fact]
    public async Task ImportProcessedSessionAsync_ExistingLegacyHash_KeepsOriginalSessionAndSource()
    {
        using var temp = new TempDatabase();
        await using var database = new TestPersistenceHarness(temp.DatabasePath);
        using var file = new TelemetryFileSource("renamed.SST", [1, 2, 3]);
        var source = RecordedSessionSourceFactory.CreateImportedSst(file);
        var legacySource = RecordedSessionSourceFactory.CreateImportedSst(file);
        legacySource.SessionId = Guid.NewGuid();
        legacySource.SourceName = "original.SST";
        var original = new Session(legacySource.SessionId, "original", "notes", null)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65)
        };
        await database.SeedProcessedSessionAsync(original, null, legacySource);
        var candidateTrack = PersistenceTestData.CreateFullTrack();

        var result = await database.SessionRepository.ImportProcessedSessionAsync(
            new Session(source.SessionId, "renamed", "replaced", null), candidateTrack, source);

        Assert.IsType<SessionImportCommitResult.Exists>(result);
        Assert.Equal(original.Id, result.Session!.Id);
        Assert.Equal("original", result.Session.Name);
        Assert.Equal("original.SST", result.Source.SourceName);
        Assert.Single(await database.SessionRepository.GetSessionsAsync());
        Assert.Null(await database.GetSessionAsync(source.SessionId));
        Assert.Null(await database.GetAsync<Track>(candidateTrack.Id));
    }

    [Fact]
    public async Task ImportProcessedSessionAsync_SyncedMetadataWithoutSource_AddsOnlySource()
    {
        using var temp = new TempDatabase();
        await using var database = new TestPersistenceHarness(temp.DatabasePath);
        using var file = new TelemetryFileSource("local.SST", [1, 2, 3]);
        var source = RecordedSessionSourceFactory.CreateImportedSst(file);
        var original = new Session(source.SessionId, "remote name", "remote notes", null, 123)
        {
            VelocityFilterWindowMilliseconds = 100
        };
        await database.PutSessionAsync(original);
        var before = SessionSnapshot.From((await database.GetSessionAsync(original.Id))!);
        var candidateTrack = PersistenceTestData.CreateFullTrack();
        var local = new Session(source.SessionId, "local name", "local notes", null, 456)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65)
        };

        var result = await database.SessionRepository.ImportProcessedSessionAsync(local, candidateTrack, source);

        Assert.IsType<SessionImportCommitResult.Inserted>(result);
        Assert.Equal(before, SessionSnapshot.From(result.Session!));
        Assert.Equal(source.Payload, (await database.GetRecordedSessionSourceAsync(source.SessionId))!.Payload);
        Assert.Null(await database.GetSessionRawPsstAsync(source.SessionId));
        Assert.Null(await database.GetAsync<Track>(candidateTrack.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImportProcessedSessionAsync_RestoresRetainedDeletedRowAtomically(bool validSource)
    {
        using var temp = new TempDatabase();
        var clock = PersistenceTestData.CreateTimeProvider();
        await using var database = new TestPersistenceHarness(temp.DatabasePath, clock);
        using var file = new TelemetryFileSource("ride.SST", [1, 2, 3]);
        var source = RecordedSessionSourceFactory.CreateImportedSst(file);
        var oldTrack = PersistenceTestData.CreateFullTrack();
        var original = new Session(source.SessionId, "old name", "old notes", null, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65),
            ProcessingFingerprintJson = "old-generation", DurationSeconds = 65,

            SourceSessionId = source.SessionId, WindowStartSeconds = 1.25, WindowEndSeconds = 66.25,
            PreviousWindowStartSeconds = 0
        };
        await database.SessionRepository.ImportProcessedSessionAsync(original, oldTrack, source);
        Assert.True((await database.SessionTransactions.DeleteSessionAsync(original.Id, TestContext.Current.CancellationToken)).SourceDeleted);
        Assert.Null(await database.GetRecordedSessionSourceAsync(original.Id));
        var connection = await database.GetInitializedConnectionAsync();
        var deleted = (await connection.FindAsync<Session>(original.Id))!;
        clock.Advance(TimeSpan.FromSeconds(1));
        var newTrack = PersistenceTestData.CreateFullTrack();
        var imported = new Session(source.SessionId, "re-imported", "new notes", null, 200)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(80),
            ProcessingFingerprintJson = "new-generation", DurationSeconds = 80,

        };
        if (!validSource) source.SourceHash = "invalid-hash";

        if (validSource)
        {
            var result = await database.SessionRepository.ImportProcessedSessionAsync(imported, newTrack, source);

            Assert.IsType<SessionImportCommitResult.Inserted>(result);
            var restored = Assert.IsAssignableFrom<Session>(result.Session);
            Assert.Equal(original.Id, restored.Id);
            Assert.Equal("re-imported", restored.Name);

            Assert.Equal(imported.ProcessedData, await database.GetSessionRawPsstAsync(restored.Id));

            Assert.NotNull(await database.GetAsync<Track>(newTrack.Id));
            Assert.Equal(source.Payload, (await database.GetRecordedSessionSourceAsync(restored.Id))!.Payload);
            Assert.Equal(deleted.Version + 1, (await connection.FindAsync<Session>(restored.Id))!.Version);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.SessionRepository.ImportProcessedSessionAsync(imported, newTrack, source));

            Assert.Null(await database.GetSessionAsync(original.Id));
            var retained = (await connection.FindAsync<Session>(original.Id))!;
            Assert.Equal(deleted.Deleted, retained.Deleted);
            Assert.Equal(original.ProcessedData, retained.ProcessedData);
            Assert.Equal("old name", retained.Name);
            Assert.Null(await database.GetRecordedSessionSourceAsync(original.Id));
            Assert.Null(await database.GetAsync<Track>(newTrack.Id));
        }
    }

    [Fact]
    public async Task ImportProcessedSessionAsync_ConcurrentImports_CommitOneRecordingAndTrack()
    {
        using var temp = new TempDatabase();
        await using var database = new TestPersistenceHarness(temp.DatabasePath);
        using var file = new TelemetryFileSource("ride.SST", [1, 2, 3]);
        var candidates = Enumerable.Range(0, 4).Select(index =>
        {
            var source = RecordedSessionSourceFactory.CreateImportedSst(file);
            var track = PersistenceTestData.CreateFullTrack();
            var session = new Session(source.SessionId, $"ride {index}", "", null)
            {
                ProcessedData = PersistenceTestData.CreateTelemetryBlob(65)
            };
            return (Session: session, Track: track, Source: source);
        }).ToArray();

        var results = await Task.WhenAll(candidates.Select(candidate =>
            database.SessionRepository.ImportProcessedSessionAsync(candidate.Session, candidate.Track, candidate.Source)));

        var imported = Assert.Single(results.Where(result => result is SessionImportCommitResult.Inserted));
        Assert.Equal(3, results.Count(result => result is SessionImportCommitResult.Exists));
        Assert.Single(await database.SessionRepository.GetSessionsAsync());
        Assert.Equal(candidates[0].Session.ProcessedData, await database.GetSessionRawPsstAsync(imported.Session!.Id));
        var persistedTracks = await Task.WhenAll(candidates.Select(candidate => database.GetAsync<Track>(candidate.Track.Id)));
        Assert.Single(persistedTracks.OfType<Track>());

    }

    [Fact]
    public async Task ProcessingOption_RollbackRequiresTheExpectedValue_AndPreservesTelemetry()
    {
        using var temp = new TempDatabase();
        await using var database = new TestPersistenceHarness(temp.DatabasePath);
        var session = new Session(Guid.NewGuid(), "ride", "notes", null, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(30),
            VelocityFilterWindowMilliseconds = 50
        };
        await database.PutAsync(session);
        await database.SessionRepository.SetProcessingOptionAsync(session.Id, 100);
        await database.SessionRepository.SetProcessingOptionAsync(session.Id, 250);

        var unchanged = await database.SessionRepository.RestoreProcessingOptionAsync(session.Id, 100, 50);
        Assert.Equal(250, unchanged!.VelocityFilterWindowMilliseconds);
        var restored = await database.SessionRepository.RestoreProcessingOptionAsync(session.Id, 250, 50);
        Assert.Equal(50, restored!.VelocityFilterWindowMilliseconds);
        Assert.Equal("notes", restored.Description);
        Assert.Equal(session.ProcessedData, await database.SessionRepository.GetSessionRawPsstAsync(session.Id));
    }

    [Fact]
    public async Task ImportProcessedSessionAsync_PersistsDurationWithoutWritingGpsMetrics()
    {
        using var tempDatabase = new TempDatabase("processed-session-as-given.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        await using var database = new TestPersistenceHarness(databasePath);
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

        var commit = await database.SessionRepository.ImportProcessedSessionAsync(session, null, PersistenceTestData.CreateRecordedSessionSource(session.Id));
        var persisted = commit.Session!;
        var loaded = await database.GetSessionAsync(sessionId);

        Assert.Equal(1, persisted.DurationSeconds);
        Assert.Null(persisted.DistanceMeters);
        Assert.Null(persisted.AscentMeters);
        Assert.Null(persisted.DescentMeters);

        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.DurationSeconds);
        Assert.Null(loaded.DistanceMeters);
        Assert.Null(loaded.AscentMeters);
        Assert.Null(loaded.DescentMeters);

    }

    [Fact]
    public async Task ImportProcessedSessionAsync_PersistsSessionTrackSourceAndFingerprint()
    {
        using var tempDatabase = new TempDatabase("processed-session.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var track = PersistenceTestData.CreateFullTrack();
        var processedData = PersistenceTestData.CreateTelemetryBlob(65);

        await using var database = new TestPersistenceHarness(databasePath);
        var session = new Session(sessionId, "processed", "desc", null, 100)
        {
            ProcessedData = processedData,
            ProcessingFingerprintJson = """{"schemaVersion":1}"""
        };
        var source = PersistenceTestData.CreateRecordedSessionSource(sessionId);

        var commit = await database.SessionRepository.ImportProcessedSessionAsync(session, track, source);
        var persisted = commit.Session!;

        Assert.True(persisted.HasProcessedData);
        Assert.Equal("""{"schemaVersion":1}""", persisted.ProcessingFingerprintJson);
        Assert.Equal(processedData, await database.GetSessionRawPsstAsync(sessionId));
        Assert.Equal(track.Points.Select(point => (point.Time, point.X, point.Y)),
            (await database.GetAsync<Track>(track.Id))!.Points.Select(point => (point.Time, point.X, point.Y)));
        var storedSource = (await database.GetRecordedSessionSourceAsync(sessionId))!;
        Assert.Equal(source.Payload, storedSource.Payload);
        Assert.Equal(source.SourceHash, storedSource.SourceHash);

    }

    [Fact]
    public async Task PutSessionAsync_PreservesExistingSummaryMetrics_OnMetadataUpdate()
    {
        using var tempDatabase = new TempDatabase("session-summary-metadata-save.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        await using var database = new TestPersistenceHarness(databasePath);
        var processed = await database.SeedProcessedSessionAsync(
            new Session(sessionId, "processed", "desc", null, 100)
            {
                ProcessedData = PersistenceTestData.CreateTelemetryBlob(65)
            },
            PersistenceTestData.CreateFullTrack(),
            source: null);

        await database.SeedGpsMetricsAsync(sessionId, new GpsMetrics(20, 2, 1));
        processed = (await database.GetSessionAsync(sessionId))!;
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
    public async Task PutSessionAsync_PreservesAlignmentTimestampAndProcessingOption_OnMetadataUpdate()
    {
        using var tempDatabase = new TempDatabase("session-gps-offset-metadata-save.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        await using var database = new TestPersistenceHarness(databasePath);

        await database.PutSessionAsync(new Session(sessionId, "original", "desc", null, 100)
        {
            GpsOffsetSeconds = 1.25, VelocityFilterWindowMilliseconds = 100
        });

        await database.PutSessionAsync(new Session(sessionId, "renamed", "desc", null, 200)
        {
            GpsOffsetSeconds = 3.75, VelocityFilterWindowMilliseconds = 250
        });

        var loaded = await database.GetSessionAsync(sessionId);

        Assert.NotNull(loaded);
        Assert.Equal(1.25, loaded!.GpsOffsetSeconds);
        Assert.Equal(100, loaded.Timestamp);
        Assert.Equal(100, loaded.VelocityFilterWindowMilliseconds);
        Assert.Equal("renamed", loaded.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateProcessedDerivedDataAsync_RollsBack_WhenInputsChangeAfterCapture(bool calibrationChange)
    {
        using var temp = new TempDatabase();
        await using var database = new TestPersistenceHarness(temp.DatabasePath);
        var bike = new Bike(Guid.NewGuid(), "bike") { HeadAngle = 64 };
        var setup = new Setup(Guid.NewGuid(), "setup") { BikeId = bike.Id };
        await database.PutAsync(bike);
        await database.PutAsync(setup);
        var original = new Session(Guid.NewGuid(), "ride", "notes", setup.Id, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65)
        };
        var source = PersistenceTestData.CreateRecordedSessionSource(original.Id);
        await database.SeedProcessedSessionAsync(original, null, source);
        var expected = (await database.GetProcessingInputBundleAsync(original.Id))!;
        if (calibrationChange)
        {
            bike.HeadAngle = 65;
            await database.PutAsync(bike);
        }
        else
        {
            await database.SessionRepository.SetProcessingOptionAsync(original.Id, 100);
        }
        var before = (await database.GetSessionAsync(original.Id))!;
        var newTrack = PersistenceTestData.CreateFullTrack();
        var recomputed = new Session(original.Id, "stale draft", "", setup.Id, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(66)
        };

        var result = await database.UpdateProcessedDerivedDataAsync(recomputed, newTrack, expected);

        Assert.Null(result);
        var current = (await database.GetSessionAsync(original.Id))!;
        Assert.Equal(before.Updated, current.Updated);
        Assert.Equal(original.ProcessedData, await database.GetSessionRawPsstAsync(original.Id));
        Assert.Null(await database.GetAsync<Track>(newTrack.Id));
    }

    [Fact]
    public async Task UpdateProcessedDerivedDataAsync_ReplacesWindowGenerationWithoutChangingAuthoredMetadata()
    {
        using var tempDatabase = new TempDatabase("processed-derived-data-window-source.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var sourceSessionId = Guid.NewGuid();
        var fingerprintService = new ProcessingFingerprintService();

        var clock = PersistenceTestData.CreateTimeProvider();
        await using var database = new TestPersistenceHarness(databasePath, clock);
        var bike = new Bike(Guid.NewGuid(), "bike")
        {
            HeadAngle = 64
        };
        var setup = new Setup(Guid.NewGuid(), "setup")
        {
            BikeId = bike.Id
        };
        var session = new Session(sessionId, "derived", "desc", setup.Id, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(60),
            SourceSessionId = sourceSessionId,
            WindowStartSeconds = 1,
            WindowEndSeconds = 2
        };
        var source = PersistenceTestData.CreateRecordedSessionSource(sourceSessionId);
        var fingerprint = CreateCurrentFingerprint(fingerprintService, session, setup, bike, source);
        session.ProcessingFingerprintJson = AppJson.Serialize(fingerprint);

        await database.PutAsync(bike);
        await database.PutAsync(setup);
        var saved = await database.SeedProcessedSessionAsync(session, newFullTrack: null, source: null);
        var authoredUpdated = saved.Updated;
        clock.Advance(TimeSpan.FromHours(1));
        await database.PutRecordedSessionSourceAsync(source);

        var recomputed = new Session(sessionId, "stale draft name", "older notes", setup.Id, 90)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(66),
            ProcessingFingerprintJson = AppJson.Serialize(fingerprint),
            DurationSeconds = 66,
            SourceSessionId = sourceSessionId,
            WindowStartSeconds = 1,
            WindowEndSeconds = 2
        };

        var result = await database.UpdateProcessedDerivedDataAsync(
            recomputed,
            newFullTrack: null,
            (await database.GetProcessingInputBundleAsync(sessionId))!);

        Assert.NotNull(result);
        Assert.Equal(66, result!.DurationSeconds);
        Assert.Equal(authoredUpdated, result.Updated);
        Assert.Equal("derived", result.Name);
        Assert.Equal("desc", result.Description);
        Assert.Equal(100, result.Timestamp);
        Assert.Equal(recomputed.ProcessedData, await database.GetSessionRawPsstAsync(sessionId));
    }

    [Fact]
    public async Task ImportProcessedSessionAsync_RollsBackSessionTrackAndSource_WhenSourceWriteFails()
    {
        using var tempDatabase = new TempDatabase("processed-session-rollback.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var track = PersistenceTestData.CreateFullTrack();

        await using var database = new TestPersistenceHarness(databasePath);
        var session = new Session(sessionId, "processed", "desc", null, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65),
            ProcessingFingerprintJson = """{"schemaVersion":1}"""
        };
        var source = PersistenceTestData.CreateRecordedSessionSource(sessionId);
        source.SourceName = null!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SessionRepository.ImportProcessedSessionAsync(session, track, source));

        Assert.Null(await database.GetSessionAsync(sessionId));
        Assert.Null(await database.GetAsync<Track>(track.Id));
        Assert.Null(await database.GetRecordedSessionSourceAsync(sessionId));

    }

    [Fact]
    public async Task UpdateSessionPsstAsync_WritesDataAndDurationPreservingGpsMetrics_WithoutBumpingUpdated()
    {
        using var tempDatabase = new TempDatabase("session-psst-update.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        await using var database = new TestPersistenceHarness(databasePath);
        await database.PutSessionAsync(new Session(sessionId, "session", "desc", null, 100));
        await database.SeedGpsMetricsAsync(sessionId, new GpsMetrics(10, 4, 2));
        var before = await database.GetSessionAsync(sessionId);
        var beforeRevision = before!.ProcessedTelemetryRevision;

        var data = PersistenceTestData.CreateTelemetryBlob(65);
        await database.SessionRepository.UpdateSessionPsstAsync(
            sessionId,
            data,
            null,
            65, SessionProcessedWriteGuard.From(SessionSnapshot.From(before!)));

        var after = await database.GetSessionAsync(sessionId);

        Assert.NotNull(after);
        Assert.Equal(65, after!.DurationSeconds);
        Assert.Equal(10, after.DistanceMeters);
        Assert.Equal(4, after.AscentMeters);
        Assert.Equal(2, after.DescentMeters);
        Assert.Equal(before!.Updated, after.Updated);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.UpdatedBy, after.UpdatedBy);
        Assert.True(after.LocalSequence > before.LocalSequence);
        Assert.True(after.SyncSequence > before.SyncSequence);
        Assert.Equal(beforeRevision + 1, after.ProcessedTelemetryRevision);
        Assert.Null(await database.SessionRepository.GetSessionRawPsstAsync(sessionId, beforeRevision));
        Assert.Equal(data, await database.SessionRepository.GetSessionRawPsstAsync(sessionId, after.ProcessedTelemetryRevision));

    }

    [Fact]
    public async Task UpdateSessionProcessedGenerationAsync_ReplacesCompleteGenerationWithoutBumpingUpdated()
    {
        using var tempDatabase = new TempDatabase("session-generation-update.db");
        var sessionId = Guid.NewGuid();
        var heldData = PersistenceTestData.CreateTelemetryBlob(65);
        var targetData = PersistenceTestData.CreateTelemetryBlob(80);
        await using var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        await database.PutSessionAsync(new Session(sessionId, "session", "desc", null, 100)
        {
            ProcessedData = heldData,
            ProcessingFingerprintJson = "held",
            DurationSeconds = 65,
            DistanceMeters = 10,
            AscentMeters = 4,
            DescentMeters = 2,

            GpsOffsetSeconds = 0.5,

        });
        await database.SeedGpsMetricsAsync(sessionId, new GpsMetrics(10, 4, 2));
        var before = (await database.GetSessionAsync(sessionId))!;
        var targetGeneration = new SessionProcessedGeneration(
            DurationSeconds: 80);

        await database.SessionRepository.UpdateSessionProcessedGenerationAsync(
            sessionId,
            targetData,
            "target",
            targetGeneration, SessionProcessedWriteGuard.From(SessionSnapshot.From(before)));

        var after = (await database.GetSessionAsync(sessionId))!;
        Assert.Equal(before.Updated, after.Updated);
        Assert.Equal("target", after.ProcessingFingerprintJson);
        Assert.Equal(80, after.DurationSeconds);
        Assert.Equal(10, after.DistanceMeters);
        Assert.Equal(4, after.AscentMeters);
        Assert.Equal(2, after.DescentMeters);

        Assert.Equal(before.GpsOffsetSeconds, after.GpsOffsetSeconds);
        Assert.Equal(before.ProcessedTelemetryRevision + 1, after.ProcessedTelemetryRevision);

        Assert.Null(await database.SessionRepository.GetSessionRawPsstAsync(
            sessionId,
            before.ProcessedTelemetryRevision));
        Assert.Equal(targetData, await database.SessionRepository.GetSessionRawPsstAsync(
            sessionId,
            after.ProcessedTelemetryRevision));

    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ProcessedWrite_RejectsAnAdvancedGeneration(bool completeGeneration, bool sameFingerprint)
    {
        using var temp = new TempDatabase();
        await using var database = new TestPersistenceHarness(temp.DatabasePath);
        var session = new Session(Guid.NewGuid(), "ride", "notes", null)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65), ProcessingFingerprintJson = "held"
        };
        await database.SeedProcessedSessionAsync(session, null, null);
        var expected = SessionProcessedWriteGuard.From(SessionSnapshot.From((await database.GetSessionAsync(session.Id))!));
        var newer = PersistenceTestData.CreateTelemetryBlob(80);
        var newerFingerprint = sameFingerprint ? "held" : "newer";
        Assert.True(await database.SessionRepository.UpdateSessionPsstAsync(session.Id, newer, newerFingerprint,
            80, expected));

        var staleBytes = PersistenceTestData.CreateTelemetryBlob(90);
        var applied = completeGeneration
            ? await database.SessionRepository.UpdateSessionProcessedGenerationAsync(session.Id, staleBytes, "target",
                new SessionProcessedGeneration(90), expected)
            : await database.SessionRepository.UpdateSessionPsstAsync(session.Id, staleBytes, "target",
                90, expected);

        Assert.False(applied);
        var retained = (await database.GetSessionAsync(session.Id))!;
        Assert.Equal(newer, await database.GetSessionRawPsstAsync(session.Id));
        Assert.Equal(newerFingerprint, retained.ProcessingFingerprintJson);
        Assert.Equal(80, retained.DurationSeconds);

    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TransferredGeneration_RejectsInputsChangedAfterValidation(bool calibrationChange, bool swap)
    {
        using var temp = new TempDatabase();
        await using var database = new TestPersistenceHarness(temp.DatabasePath);
        var bike = new Bike(Guid.NewGuid(), "bike") { HeadAngle = 64 };
        var setup = new Setup(Guid.NewGuid(), "setup") { BikeId = bike.Id };
        await database.PutAsync(bike);
        await database.PutAsync(setup);
        var original = new Session(Guid.NewGuid(), "ride", "notes", setup.Id, 100)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65)
        };
        await database.SeedProcessedSessionAsync(original, null, PersistenceTestData.CreateRecordedSessionSource(original.Id));
        var held = (await database.GetSessionAsync(original.Id))!;
        var guard = SessionProcessedWriteGuard.From(SessionSnapshot.From(held)) with
        {
            ExpectedInputs = (await database.GetProcessingInputBundleAsync(original.Id))!
        };
        if (calibrationChange)
        {
            bike.HeadAngle = 65;
            await database.PutAsync(bike);
        }
        else await database.SessionRepository.SetProcessingOptionAsync(original.Id, 100);
        var bytes = PersistenceTestData.CreateTelemetryBlob(80);

        var committed = swap
            ? await database.SessionRepository.UpdateSessionProcessedGenerationAsync(original.Id, bytes, "target", new SessionProcessedGeneration(80), guard)
            : await database.SessionRepository.UpdateSessionPsstAsync(original.Id, bytes, "target", 80, guard);

        Assert.False(committed);
        Assert.Equal(original.ProcessedData, await database.GetSessionRawPsstAsync(original.Id));
        Assert.Equal(held.ProcessedTelemetryRevision, (await database.GetSessionAsync(original.Id))!.ProcessedTelemetryRevision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessedWrite_CompletesOnlyTheCapturedHubRequest(bool requestChanged)
    {
        using var temp = new TempDatabase();
        await using var database = new TestPersistenceHarness(temp.DatabasePath);
        var session = new Session(Guid.NewGuid(), "ride", "notes", null)
        {
            ProcessedData = PersistenceTestData.CreateTelemetryBlob(65), ProcessingFingerprintJson = "held"
        };
        await database.SeedProcessedSessionAsync(session, null, null);
        var expected = SessionProcessedWriteGuard.From(SessionSnapshot.From((await database.GetSessionAsync(session.Id))!), "target");
        var connection = await database.GetInitializedConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO session_blob_swap_request (session_id, target_fingerprint, target_generation) VALUES (?, ?, ?)",
            session.Id, requestChanged ? "new-target" : "target", AppJson.Serialize(new SessionProcessedGeneration(80)));
        var incoming = PersistenceTestData.CreateTelemetryBlob(80);

        var applied = await database.SessionRepository.UpdateSessionProcessedGenerationAsync(session.Id, incoming, "target",
            new SessionProcessedGeneration(80), expected);

        Assert.Equal(!requestChanged, applied);
        var pending = await database.GetSessionBlobSwapRequestAsync(session.Id);
        if (requestChanged)
        {
            Assert.Equal("new-target", pending!.TargetFingerprint);
            Assert.Equal(session.ProcessedData, await database.GetSessionRawPsstAsync(session.Id));
        }
        else
        {
            Assert.Null(pending);
            Assert.Equal(incoming, await database.GetSessionRawPsstAsync(session.Id));
        }
    }

    [Fact]
    public async Task UpdateSessionPsstAsync_ReturnsFalse_WhenSessionDoesNotExist()
    {
        using var tempDatabase = new TempDatabase("session-psst-update-missing.db");
        var databasePath = tempDatabase.DatabasePath;

        await using var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        Assert.False(await database.SessionRepository.UpdateSessionPsstAsync(
            Guid.NewGuid(),
            [1, 2, 3],
            null,
            null, new SessionProcessedWriteGuard(null, 0)));

    }

    [Fact]
    public async Task GetChangedAsync_ForSessions_DerivesHasProcessedDataFromDataColumn()
    {
        using var tempDatabase = new TempDatabase("session-has-data-derived.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        await using var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "session", "desc", null, 100)
            {
                ProcessedData = PersistenceTestData.CreateTelemetryBlob(65),
                Updated = 10, LocalSequence = 1
            });
        }

        var changedSession = Assert.Single(await database.GetChangedAsync<Session>(0, long.MaxValue));

        Assert.True(changedSession.HasProcessedData);
        Assert.Null(changedSession.ProcessedData);

    }

    [Fact]
    public async Task PutSessionAsync_ReusesSoftDeletedRow_AndPreservesExistingDerivedData()
    {
        using var tempDatabase = new TempDatabase("session-revive.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var originalPsst = new byte[] { 9, 8, 7 };

        await using var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        // A soft-deleted row that was fully processed: it carries the derived
        // columns (PSST blob, full-track linkage, fingerprint).
        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "old", "old desc", null, 50)
            {
                ProcessedData = originalPsst,

                ProcessingFingerprintJson = """{"existing":true}""",
                Updated = 10,

                Deleted = 10
            });
        }

        // The metadata save carries only user-authored metadata. Any derived
        // columns set on the incoming model are ignored: PutSessionAsync does not
        // name data and never touches the full-track linkage or fingerprint
        // owned by the processed-write pipeline.
        await database.PutSessionAsync(new Session(sessionId, "new", "new desc", setupId, 1234)
        {
            ProcessingFingerprintJson = """{"incoming":true}"""
        });

        var session = await database.GetSessionAsync(sessionId);
        var rawPsst = await database.GetSessionRawPsstAsync(sessionId);

        Assert.NotNull(session);
        Assert.Equal("new", session!.Name);
        Assert.Equal("new desc", session.Description);
        Assert.Equal(setupId, session.Setup);
        Assert.Equal(50, session.Timestamp);
        Assert.Null(session.Deleted);
        // Derived columns are preserved from the existing row, not taken from the
        // incoming metadata save.

        Assert.Equal("""{"existing":true}""", session.ProcessingFingerprintJson);
        Assert.True(session.HasProcessedData);
        Assert.Equal(1, session.ProcessedTelemetryRevision);

        Assert.Equal(originalPsst, rawPsst);

    }

    [Fact]
    public async Task GetActiveSessionIdsAsync_ReturnsLiveIdsOrdered_WithoutDecodingData()
    {
        using var tempDatabase = new TempDatabase("active-session-ids.db");
        var databasePath = tempDatabase.DatabasePath;
        var olderId = Guid.NewGuid();
        var newerId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();

        await using var database = new TestPersistenceHarness(databasePath);
        await database.PutSessionAsync(new Session(olderId, "older", "desc", null, 100));
        await database.PutSessionAsync(new Session(newerId, "newer", "desc", null, 200));
        await database.PutSessionAsync(new Session(deletedId, "deleted", "desc", null, 300));

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Execute("UPDATE session SET data = ? WHERE id IN (?, ?)", new byte[] { 1, 2, 3 }, olderId, newerId);
            connection.Execute("UPDATE session SET deleted = ? WHERE id = ?", 123, deletedId);
        }

        var ids = await database.SessionRepository.GetActiveSessionIdsAsync();

        Assert.Equal([newerId, olderId], ids);
    }

    [Fact]
    public async Task UpdateGpsAlignmentAsync_WritesOffsetPreservingGpsMetrics_AndBumpsUpdated()
    {
        using var tempDatabase = new TempDatabase("session-track-update.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        await using var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "session", "desc", null, 100)
            {
                DurationSeconds = 65,
                Updated = 1,

            });
        }

        await database.SeedGpsMetricsAsync(sessionId, new GpsMetrics(1.5, 0, 0));
        var before = await database.GetSessionAsync(sessionId);

        await database.SessionRepository.UpdateGpsAlignmentAsync(
            sessionId, 1.25);

        var after = await database.GetSessionAsync(sessionId);
        Assert.Equal(1.25, after!.GpsOffsetSeconds);

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(65, after!.DurationSeconds);
        Assert.Equal(1.5, after.DistanceMeters);
        Assert.Equal(0, after.AscentMeters);
        Assert.Equal(0, after.DescentMeters);
        Assert.True(after!.Updated > before!.Updated);

    }

    private static ProcessingFingerprint CreateCurrentFingerprint(
        ProcessingFingerprintService fingerprintService,
        Session session,
        Setup setup,
        Bike bike,
        RecordedSessionSource source) =>
        fingerprintService.CreateCurrent(
            SessionSnapshot.From(session),
            SetupSnapshot.From(setup),
            BikeSnapshot.From(bike),
            RecordedSessionSourceSnapshot.From(source));
}
