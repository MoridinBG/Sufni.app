using System.Diagnostics.CodeAnalysis;
using SQLite;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Models;
using Sufni.App.ExtensionHost.SessionDetails;
using Sufni.App.ExtensionHosting.Database;

namespace Sufni.App.Tests.Services;

public class SqlitePersistenceTests
{
    [Fact]
    public async Task UpdateLastSyncTimeAsync_InsertsRow_WhenMissing()
    {
        using var tempDatabase = new TempDatabase("sync.db");
        var databasePath = tempDatabase.DatabasePath;

        var database = new TestPersistenceHarness(databasePath);

        await database.UpdateLastSyncTimeAsync("https://sync.test");

        var lastSyncTime = await database.GetLastSyncTimeAsync("https://sync.test");
        Assert.True(lastSyncTime > 0);

        using var connection = new SQLiteConnection(databasePath);
        var rows = connection.Table<Synchronization>()
            .Where(s => s.ServerUrl == "https://sync.test")
            .ToList();
        Assert.Single(rows);
    }

    [Fact]
    public async Task UpdateLastSyncTimeAsync_UpdatesExistingRow_WithoutDuplicatingIt()
    {
        using var tempDatabase = new TempDatabase("sync.db");
        var databasePath = tempDatabase.DatabasePath;

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetLastSyncTimeAsync("https://sync.test");

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Synchronization
            {
                ServerUrl = "https://sync.test",
                LastSyncTime = 1
            });
        }

        await database.UpdateLastSyncTimeAsync("https://sync.test");

        var lastSyncTime = await database.GetLastSyncTimeAsync("https://sync.test");
        Assert.True(lastSyncTime > 1);

        using var verificationConnection = new SQLiteConnection(databasePath);
        var rows = verificationConnection.Table<Synchronization>()
            .Where(s => s.ServerUrl == "https://sync.test")
            .ToList();
        Assert.Single(rows);
    }

    [Fact]
    public async Task Initialization_BackfillsLegacyLinkageRows_AndKeepsBackfillIdempotent()
    {
        using var tempDatabase = new TempDatabase("legacy.db");
        var databasePath = tempDatabase.DatabasePath;
        var legacyBikeId = Guid.NewGuid();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Execute(
                """
                CREATE TABLE bike (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    head_angle REAL NOT NULL,
                    fork_stroke REAL,
                    shock_stroke REAL,
                    linkage TEXT,
                    pixels_to_millimeters REAL NOT NULL DEFAULT 0,
                    front_wheel_diameter REAL,
                    rear_wheel_diameter REAL,
                    front_wheel_rim_size INTEGER,
                    front_wheel_tire_width REAL,
                    rear_wheel_rim_size INTEGER,
                    rear_wheel_tire_width REAL,
                    image_rotation_degrees REAL NOT NULL DEFAULT 0,
                    image BLOB,
                    updated INTEGER NOT NULL,
                    client_updated INTEGER,
                    deleted INTEGER
                )
                """);

            connection.Execute(
                "INSERT INTO bike (id, name, head_angle, fork_stroke, shock_stroke, linkage, pixels_to_millimeters, image_rotation_degrees, image, updated) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                legacyBikeId.ToString(),
                "legacy linkage bike",
                64.0,
                150.0,
                0.5,
                TestSnapshots.FullSuspensionLinkage().ToJson(),
                0.0,
                0.0,
                Array.Empty<byte>(),
                1);
        }

        var firstRun = new TestPersistenceHarness(databasePath);
        var firstRunBikes = await firstRun.GetAllAsync<Bike>();

        using (var verificationConnection = new SQLiteConnection(databasePath))
        {
            var columns = verificationConnection.Query<TableColumnInfo>("PRAGMA table_info(bike)");

            Assert.Contains(columns, column => column.Name == "rear_suspension_kind");
            Assert.Contains(columns, column => column.Name == "leverage_ratio");
            Assert.Contains(columns, column => column.Name == "front_compression_damping_cutoff_mm_per_second");
            Assert.Contains(columns, column => column.Name == "front_rebound_damping_cutoff_mm_per_second");
            Assert.Contains(columns, column => column.Name == "rear_compression_damping_cutoff_mm_per_second");
            Assert.Contains(columns, column => column.Name == "rear_rebound_damping_cutoff_mm_per_second");
        }

        var firstRunBike = Assert.Single(firstRunBikes);
        Assert.Equal(legacyBikeId, firstRunBike.Id);
        Assert.Equal(RearSuspensionKind.Linkage, firstRunBike.RearSuspensionKind);
        Assert.Equal(DampingSpeedCutoffs.DefaultMmPerSecond, firstRunBike.FrontCompressionDampingCutoffMmPerSecond);
        Assert.Equal(DampingSpeedCutoffs.DefaultMmPerSecond, firstRunBike.FrontReboundDampingCutoffMmPerSecond);
        Assert.Equal(DampingSpeedCutoffs.DefaultMmPerSecond, firstRunBike.RearCompressionDampingCutoffMmPerSecond);
        Assert.Equal(DampingSpeedCutoffs.DefaultMmPerSecond, firstRunBike.RearReboundDampingCutoffMmPerSecond);
        Assert.NotNull(firstRunBike.Linkage);

        var secondRun = new TestPersistenceHarness(databasePath);
        var secondRunBikes = await secondRun.GetAllAsync<Bike>();

        var secondRunBike = Assert.Single(secondRunBikes);
        Assert.Equal(legacyBikeId, secondRunBike.Id);
        Assert.Equal(RearSuspensionKind.Linkage, secondRunBike.RearSuspensionKind);
        Assert.Equal(DampingSpeedCutoffs.DefaultMmPerSecond, secondRunBike.FrontCompressionDampingCutoffMmPerSecond);
        Assert.Equal(DampingSpeedCutoffs.DefaultMmPerSecond, secondRunBike.FrontReboundDampingCutoffMmPerSecond);
        Assert.Equal(DampingSpeedCutoffs.DefaultMmPerSecond, secondRunBike.RearCompressionDampingCutoffMmPerSecond);
        Assert.Equal(DampingSpeedCutoffs.DefaultMmPerSecond, secondRunBike.RearReboundDampingCutoffMmPerSecond);

    }

    [Fact]
    public async Task Initialization_CreatesReactiveSessionPersistenceSchema()
    {
        using var tempDatabase = new TempDatabase("reactive-schema.db");
        var databasePath = tempDatabase.DatabasePath;

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using var connection = new SQLiteConnection(databasePath);
        var sessionColumns = connection.Query<TableColumnInfo>("PRAGMA table_info(session)");
        var sourceColumns = connection.Query<TableColumnInfo>("PRAGMA table_info(session_recording_source)");

        Assert.Contains(sessionColumns, column => column.Name == "session_processing_fingerprint");
        Assert.Contains(sessionColumns, column => column.Name == "duration_seconds");
        Assert.Contains(sessionColumns, column => column.Name == "distance_meters");
        Assert.Contains(sessionColumns, column => column.Name == "ascent_meters");
        Assert.Contains(sessionColumns, column => column.Name == "descent_meters");
        Assert.Contains(sourceColumns, column => column.Name == "session_id");
        Assert.Contains(sourceColumns, column => column.Name == "source_kind");
        Assert.Contains(sourceColumns, column => column.Name == "source_name");
        Assert.Contains(sourceColumns, column => column.Name == "schema_version");
        Assert.Contains(sourceColumns, column => column.Name == "source_hash");
        Assert.Contains(sourceColumns, column => column.Name == "payload");
    }

    [Fact]
    public async Task Initialization_CreatesExtensionSchemaVersionTable()
    {
        using var tempDatabase = new TempDatabase("extension-schema-version.db");
        var databasePath = tempDatabase.DatabasePath;

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetAllAsync<Board>();

        using var connection = new SQLiteConnection(databasePath);
        var tables = connection.Query<SqliteMasterRow>(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'extension_schema_version'");

        Assert.Single(tables);
    }

    [Fact]
    public async Task Initialization_CreatesExtensionMigratorTables()
    {
        using var tempDatabase = new TempDatabase("extension-table.db");
        var databasePath = tempDatabase.DatabasePath;

        var migrator = new TestExtensionMigrator(
            "test",
            targetVersion: 0,
            [typeof(TestExtensionRow)],
            []);
        var context = CreateConnectionContext(databasePath, [migrator]);

        _ = await context.GetInitializedConnectionAsync();

        using var connection = new SQLiteConnection(databasePath);
        var tables = connection.Query<SqliteMasterRow>(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'test_extension_row'");

        Assert.Single(tables);
    }

    [Fact]
    public void Constructor_RejectsDuplicateExtensionMigratorIds()
    {
        using var tempDatabase = new TempDatabase("duplicate-extension-id.db");
        var databasePath = tempDatabase.DatabasePath;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new TestPersistenceHarness(
                databasePath,
                [
                    new TestExtensionMigrator("test", targetVersion: 0, [typeof(TestExtensionRow)], []),
                    new TestExtensionMigrator("test", targetVersion: 0, [typeof(SecondTestExtensionRow)], []),
                ]));

        Assert.Contains("test", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsDuplicateExtensionTableOwnership()
    {
        using var tempDatabase = new TempDatabase("duplicate-extension-table.db");
        var databasePath = tempDatabase.DatabasePath;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new TestPersistenceHarness(
                databasePath,
                [
                    new TestExtensionMigrator("first", targetVersion: 0, [typeof(TestExtensionRow)], []),
                    new TestExtensionMigrator("second", targetVersion: 0, [typeof(DuplicateNamedExtensionRow)], []),
                ]));

        Assert.Contains("test_extension_row", exception.Message);
        Assert.Contains("first", exception.Message);
        Assert.Contains("second", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsExtensionTablesThatUseReservedCoreTableNames()
    {
        using var tempDatabase = new TempDatabase("reserved-extension-table.db");
        var databasePath = tempDatabase.DatabasePath;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new TestPersistenceHarness(
                databasePath,
                [new TestExtensionMigrator("test", targetVersion: 0, [typeof(CoreNamedExtensionRow)], [])]));

        Assert.Contains("session", exception.Message);
        Assert.Contains("reserved", exception.Message);
    }

    [Fact]
    public async Task Initialization_RunsExtensionMigrationStepsOnceAndAdvancesVersion()
    {
        using var tempDatabase = new TempDatabase("extension-migrations.db");
        var databasePath = tempDatabase.DatabasePath;
        var appliedSteps = new List<int>();
        var migrator = new TestExtensionMigrator(
            "test",
            targetVersion: 2,
            [typeof(TestExtensionRow)],
            [
                new ExtensionDatabaseMigrationStep(1, async (context, _) =>
                {
                    appliedSteps.Add(1);
                    await context.Database.InsertAsync(new TestExtensionRow
                    {
                        Id = "step-1",
                        Value = 1,
                    });
                }),
                new ExtensionDatabaseMigrationStep(2, async (context, _) =>
                {
                    appliedSteps.Add(2);
                    await context.Database.InsertAsync(new TestExtensionRow
                    {
                        Id = "step-2",
                        Value = 2,
                    });
                }),
            ]);

        var firstRun = CreateConnectionContext(databasePath, [migrator]);
        _ = await firstRun.GetInitializedConnectionAsync();

        var secondRun = CreateConnectionContext(databasePath, [migrator]);
        _ = await secondRun.GetInitializedConnectionAsync();

        using var connection = new SQLiteConnection(databasePath);
        var version = Assert.Single(connection.Table<ExtensionSchemaVersion>().ToList());
        var rows = connection.Table<TestExtensionRow>().OrderBy(row => row.Value).ToList();

        Assert.Equal([1, 2], appliedSteps);
        Assert.Equal("test", version.ExtensionId);
        Assert.Equal(2, version.Version);
        Assert.Equal(["step-1", "step-2"], rows.Select(row => row.Id).ToList());

    }

    [Fact]
    public async Task OpenSessionAsync_WaitsForExtensionMigrations()
    {
        using var tempDatabase = new TempDatabase("extension-raw-connection.db");
        var databasePath = tempDatabase.DatabasePath;
        var migrator = new TestExtensionMigrator(
            "test",
            targetVersion: 1,
            [typeof(TestExtensionRow)],
            [
                new ExtensionDatabaseMigrationStep(1, async (context, _) =>
                {
                    await context.Database.InsertAsync(new TestExtensionRow
                    {
                        Id = "ready",
                        Value = 1,
                    });
                }),
            ]);

        IExtensionDatabaseConnection database = new ExtensionDatabaseConnection(
            CreateConnectionContext(databasePath, [migrator]));

        var session = await database.OpenSessionAsync();
        var rows = await session.Table<TestExtensionRow>().ToListAsync();

        Assert.Single(rows);
        Assert.Equal("ready", rows[0].Id);

    }

    [Fact]
    public async Task OpenSessionAsync_RejectsUndeclaredAndCoreTableTypes()
    {
        using var tempDatabase = new TempDatabase("extension-owned-session.db");
        var databasePath = tempDatabase.DatabasePath;

        IExtensionDatabaseConnection database = new ExtensionDatabaseConnection(
            CreateConnectionContext(
                databasePath,
                [new TestExtensionMigrator("test", targetVersion: 0, [typeof(TestExtensionRow)], [])]));
        var session = await database.OpenSessionAsync();

        _ = session.Table<TestExtensionRow>();
        var undeclaredException = Assert.Throws<InvalidOperationException>(
            () => session.Table<SecondTestExtensionRow>());
        var coreException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.InsertAsync(new CoreNamedExtensionRow { Id = "core" }));

        Assert.Contains(typeof(SecondTestExtensionRow).FullName!, undeclaredException.Message);
        Assert.Contains(typeof(CoreNamedExtensionRow).FullName!, coreException.Message);
    }

    [Fact]
    public async Task Initialization_AddsFingerprintColumnToLegacySessionTable()
    {
        using var tempDatabase = new TempDatabase("legacy-session-fingerprint.db");
        var databasePath = tempDatabase.DatabasePath;

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Execute(
                """
                CREATE TABLE session (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    setup_id TEXT,
                    description TEXT NOT NULL,
                    timestamp INTEGER,
                    full_track_id TEXT,
                    track TEXT,
                    data BLOB,
                    front_springrate TEXT,
                    front_hsc INTEGER,
                    front_lsc INTEGER,
                    front_lsr INTEGER,
                    front_hsr INTEGER,
                    rear_springrate TEXT,
                    rear_hsc INTEGER,
                    rear_lsc INTEGER,
                    rear_lsr INTEGER,
                    rear_hsr INTEGER,
                    updated INTEGER NOT NULL,
                    client_updated INTEGER,
                    deleted INTEGER,
                    has_data INTEGER NOT NULL DEFAULT 0
                )
                """);
        }

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using var verificationConnection = new SQLiteConnection(databasePath);
        var columns = verificationConnection.Query<TableColumnInfo>("PRAGMA table_info(session)");

        Assert.Contains(columns, column => column.Name == "session_processing_fingerprint");
        Assert.Contains(columns, column => column.Name == "duration_seconds");
        Assert.Contains(columns, column => column.Name == "distance_meters");
        Assert.Contains(columns, column => column.Name == "ascent_meters");
        Assert.Contains(columns, column => column.Name == "descent_meters");
    }

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

        var source = CreateRecordedSessionSource(sessionId);

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
        var matchingSource = CreateRecordedSessionSource(matchingSessionId);
        var staleSource = CreateRecordedSessionSource(staleSessionId);
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
        var source = CreateRecordedSessionSource(sessionId);
        source.SourceHash = "not-the-payload-hash";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.PutRecordedSessionSourceAsync(source));

        Assert.Null(await database.GetRecordedSessionSourceAsync(sessionId));

    }

    [Fact]
    public async Task PutProcessedSessionAsync_PersistsSessionSummaryMetrics()
    {
        using var tempDatabase = new TempDatabase("processed-session-summary.db");
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
            ProcessedData = CreateTelemetryBlob(65)
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
    public async Task PutProcessedSessionAsync_PersistsSessionTrackSourceAndFingerprint()
    {
        using var tempDatabase = new TempDatabase("processed-session.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var track = CreateFullTrack();

        var database = new TestPersistenceHarness(databasePath);
        var session = new Session(sessionId, "processed", "desc", null, 100)
        {
            ProcessedData = [8, 7, 6],
            ProcessingFingerprintJson = """{"schemaVersion":1}"""
        };
        var source = CreateRecordedSessionSource(sessionId);

        var persisted = await database.PutProcessedSessionAsync(session, track, source);

        Assert.Equal(track.Id, persisted.FullTrack);
        Assert.True(persisted.HasProcessedData);
        Assert.Equal("""{"schemaVersion":1}""", persisted.ProcessingFingerprintJson);
        Assert.Equal([8, 7, 6], await database.GetSessionRawPsstAsync(sessionId));
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
                ProcessedData = CreateTelemetryBlob(65)
            },
            CreateFullTrack(),
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
    public async Task PutProcessedSessionAsync_AssociatesExistingTrack_WhenSessionHasNoGeneratedTrack()
    {
        using var tempDatabase = new TempDatabase("processed-session-existing-track.db");
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
            ProcessedData = [8, 7, 6],
            ProcessingFingerprintJson = """{"schemaVersion":1}"""
        };

        var persisted = await database.PutProcessedSessionAsync(session, newFullTrack: null, source: null);

        Assert.Equal(trackId, persisted.FullTrack);
        Assert.Single(await database.GetAllAsync<Track>());

    }

    [Fact]
    public async Task PutProcessedSessionIfUnchangedAsync_ReturnsNullAndRollsBack_WhenBaselineDoesNotMatch()
    {
        using var tempDatabase = new TempDatabase("processed-session-conflict.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var newTrack = CreateFullTrack();

        var database = new TestPersistenceHarness(databasePath);
        var original = new Session(sessionId, "original", "desc", null, 100)
        {
            ProcessedData = [1, 2, 3],
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
            ProcessedData = [8, 7, 6],
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
        var track = CreateFullTrack();

        var database = new TestPersistenceHarness(databasePath);
        var session = new Session(sessionId, "processed", "desc", null, 100)
        {
            ProcessedData = [8, 7, 6],
            ProcessingFingerprintJson = """{"schemaVersion":1}"""
        };
        var source = CreateRecordedSessionSource(sessionId);
        source.SourceName = null!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.PutProcessedSessionAsync(session, track, source));

        Assert.Null(await database.GetSessionAsync(sessionId));
        Assert.Null(await database.GetAsync<Track>(track.Id));
        Assert.Null(await database.GetRecordedSessionSourceAsync(sessionId));

    }

    [Fact]
    public async Task GetSynchronizationDataAsync_IncludesTrackReferencedByChangedSession()
    {
        using var tempDatabase = new TempDatabase("sync-data.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var trackId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Track
            {
                Id = trackId,
                Points =
                [
                    new TrackPoint(100, 1, 1, 10),
                    new TrackPoint(101, 2, 2, 11)
                ],
                Updated = 50,
                ClientUpdated = 50
            });

            connection.Insert(new Session(sessionId, "session", "desc", null, 100)
            {
                FullTrack = trackId,
                Updated = 150,
                ClientUpdated = 150
            });
        }

        var syncData = await database.GetSynchronizationDataAsync(100);

        Assert.Single(syncData.Sessions);
        Assert.Single(syncData.Tracks);
        Assert.Equal(trackId, syncData.Tracks[0].Id);

    }

    [Fact]
    public async Task PutAsync_Throws_WhenLiveTrackHasNoPoints()
    {
        using var tempDatabase = new TempDatabase("track-validation.db");
        var databasePath = tempDatabase.DatabasePath;

        var database = new TestPersistenceHarness(databasePath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.PutAsync(new Track { Points = [] }));
    }

    [Fact]
    public async Task FindTrackByTimeRangeAsync_ReturnsActiveTrackWithSameStartAndEnd()
    {
        using var tempDatabase = new TempDatabase("track-time-range.db");
        var databasePath = tempDatabase.DatabasePath;
        var trackId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        await database.PutAsync(new Track
        {
            Id = trackId,
            Points =
            [
                new TrackPoint(100, 1, 1, 10),
                new TrackPoint(101, 2, 2, 11)
            ]
        });

        var found = await database.FindTrackByTimeRangeAsync(100, 101);
        var missing = await database.FindTrackByTimeRangeAsync(100, 102);

        Assert.Equal(trackId, found);
        Assert.Null(missing);

    }

    [Fact]
    public async Task StartupCleanup_SoftDeletesDuplicateTrackTimeRanges_AndRepointsSessions()
    {
        using var tempDatabase = new TempDatabase("duplicate-track-cleanup.db");
        var databasePath = tempDatabase.DatabasePath;
        var canonicalTrackId = Guid.NewGuid();
        var duplicateTrackId = Guid.NewGuid();
        var canonicalSessionId = Guid.NewGuid();
        var duplicateSessionId = Guid.NewGuid();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.CreateTable<Track>();
            connection.CreateTable<Session>();
            connection.Insert(new Track
            {
                Id = canonicalTrackId,
                Points =
                [
                    new TrackPoint(100, 1, 1, 10),
                    new TrackPoint(101, 2, 2, 11)
                ],
                Updated = 10,
                ClientUpdated = 10
            });
            connection.Insert(new Track
            {
                Id = duplicateTrackId,
                Points =
                [
                    new TrackPoint(100, 3, 3, 12),
                    new TrackPoint(101, 4, 4, 13)
                ],
                Updated = 20,
                ClientUpdated = 20
            });
            connection.Insert(new Session(canonicalSessionId, "canonical", "desc", null, 100)
            {
                FullTrack = canonicalTrackId,
                Updated = 10,
                ClientUpdated = 10
            });
            connection.Insert(new Session(duplicateSessionId, "duplicate", "desc", null, 100)
            {
                FullTrack = duplicateTrackId,
                Track =
                [
                    new TrackPoint(100, 3, 3, 12),
                    new TrackPoint(101, 4, 4, 13)
                ],
                Updated = 20,
                ClientUpdated = 20
            });
        }

        var database = new TestPersistenceHarness(databasePath);

        var activeTracks = await database.GetAllAsync<Track>();
        var duplicateSession = await database.GetSessionAsync(duplicateSessionId);

        Assert.Single(activeTracks);
        Assert.Equal(canonicalTrackId, activeTracks[0].Id);
        Assert.NotNull(duplicateSession);
        Assert.Equal(canonicalTrackId, duplicateSession!.FullTrack);
        Assert.Null(duplicateSession.Track);

        using var verificationConnection = new SQLiteConnection(databasePath);
        var duplicateTrack = verificationConnection.Table<Track>().Single(track => track.Id == duplicateTrackId);
        Assert.NotNull(duplicateTrack.Deleted);

    }

    [Fact]
    public async Task ApplyRemoteSynchronizationDataAsync_AllowsDeletedTrackWithoutPoints()
    {
        using var tempDatabase = new TempDatabase("deleted-track-sync.db");
        var databasePath = tempDatabase.DatabasePath;
        var trackId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);

        await database.ApplyRemoteSynchronizationDataAsync(new SynchronizationData
        {
            Tracks =
            [
                new Track
                {
                    Id = trackId,
                    Points = [],
                    Deleted = 100,
                    Updated = 100,
                    ClientUpdated = 100
                }
            ]
        });

        using var verificationConnection = new SQLiteConnection(databasePath);
        var track = verificationConnection.Table<Track>().Single(t => t.Id == trackId);

        Assert.Equal(100, track.Deleted);
        Assert.False(track.HasPoints);

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
    public async Task PatchSessionPsstAsync_UpdatesDurationSummaryMetric()
    {
        using var tempDatabase = new TempDatabase("session-psst-summary-patch.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        await database.PutSessionAsync(new Session(sessionId, "session", "desc", null, 100)
        {
            DistanceMeters = 10,
            AscentMeters = 4,
            DescentMeters = 2
        });

        await database.PatchSessionPsstAsync(sessionId, CreateTelemetryBlob(65));

        var session = await database.GetSessionAsync(sessionId);

        Assert.NotNull(session);
        Assert.Equal(65, session!.DurationSeconds);
        Assert.Equal(10, session.DistanceMeters);
        Assert.Equal(4, session.AscentMeters);
        Assert.Equal(2, session.DescentMeters);

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

        var patchedPsst = CreateTelemetryBlob(65);
        await database.PatchSessionPsstAsync(sessionId, patchedPsst);

        var changedSession = Assert.Single(await database.GetChangedAsync<Session>(0));

        Assert.True(changedSession.HasProcessedData);
        Assert.Equal(patchedPsst, await database.GetSessionRawPsstAsync(sessionId));

    }

    [Fact]
    public async Task PatchSessionPsstAsync_RejectsInvalidBlob_WithoutChangingExistingData()
    {
        using var tempDatabase = new TempDatabase("session-psst-invalid-patch.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var originalPsst = CreateTelemetryBlob(65);

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
                ProcessedData = CreateTelemetryBlob(65),
                Updated = 10
            });
            connection.Execute("UPDATE session SET has_data = 0 WHERE id = ?", sessionId);
        }

        var changedSession = Assert.Single(await database.GetChangedAsync<Session>(0));

        Assert.True(changedSession.HasProcessedData);
        Assert.Null(changedSession.ProcessedData);

    }

    [Fact]
    public async Task ApplyRemoteSynchronizationDataAsync_UpdatesSessionSyncFields_WithoutClearingPsst()
    {
        using var tempDatabase = new TempDatabase("remote-sync.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var originalPsst = new byte[] { 1, 2, 3, 4 };

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "local", "local desc", null, 50)
            {
                ProcessedData = originalPsst,
                Updated = 1,
                ClientUpdated = 1
            });
        }

        var remoteSession = new Session(sessionId, "remote", "remote desc", setupId, 1234)
        {
            FullTrack = trackId,
            Track =
            [
                new TrackPoint(1234, 1, 1, 100),
                new TrackPoint(1235, 2, 2, 101)
            ],
            ProcessingFingerprintJson = """{"remote":true}""",
            DurationSeconds = 65,
            DistanceMeters = 10,
            AscentMeters = 4,
            DescentMeters = 2,
            FrontSpringRate = "50",
            RearSpringRate = "60",
            Updated = 99,
            ClientUpdated = 88
        };

        var remoteTrack = new Track
        {
            Id = trackId,
            Points =
            [
                new TrackPoint(1234, 10, 10, 100),
                new TrackPoint(1235, 20, 20, 101)
            ],
            Updated = 99,
            ClientUpdated = 88
        };

        await database.ApplyRemoteSynchronizationDataAsync(new SynchronizationData
        {
            Sessions = [remoteSession],
            Tracks = [remoteTrack]
        });

        var session = await database.GetSessionAsync(sessionId);
        var sessionTrack = await database.GetSessionTrackAsync(sessionId);
        var rawPsst = await database.GetSessionRawPsstAsync(sessionId);
        var fullTrack = await database.GetAsync<Track>(trackId);

        Assert.NotNull(session);
        Assert.Equal("remote", session!.Name);
        Assert.Equal("remote desc", session.Description);
        Assert.Equal(setupId, session.Setup);
        Assert.Equal(1234, session.Timestamp);
        Assert.Equal(trackId, session.FullTrack);
        Assert.Equal("""{"remote":true}""", session.ProcessingFingerprintJson);
        Assert.Equal(65, session.DurationSeconds);
        Assert.Equal(10, session.DistanceMeters);
        Assert.Equal(4, session.AscentMeters);
        Assert.Equal(2, session.DescentMeters);
        Assert.Equal(99, session.Updated);
        Assert.Equal("50", session.FrontSpringRate);
        Assert.Equal("60", session.RearSpringRate);
        Assert.NotNull(sessionTrack);
        Assert.Equal(2, sessionTrack!.Count);
        Assert.Equal(originalPsst, rawPsst);
        Assert.NotNull(fullTrack);
        Assert.Equal(2, fullTrack!.Points.Count);

    }

    [Fact]
    public async Task MergeAllAsync_UpdatesSessionSyncFields_WithoutClearingPsst()
    {
        using var tempDatabase = new TempDatabase("merge-session-sync.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var originalPsst = new byte[] { 1, 2, 3, 4 };

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "local", "local desc", null, 50)
            {
                ProcessedData = originalPsst,
                Updated = 1,
                ClientUpdated = 1
            });
        }

        await database.MergeAllAsync(new SynchronizationData
        {
            Sessions =
            [
                new Session(sessionId, "remote", "remote desc", setupId, 1234)
                {
                    FullTrack = trackId,
                    Track =
                    [
                        new TrackPoint(1234, 1, 1, 100),
                        new TrackPoint(1235, 2, 2, 101)
                    ],
                    ProcessingFingerprintJson = """{"remote":true}""",
                    DurationSeconds = 65,
                    DistanceMeters = 10,
                    AscentMeters = 4,
                    DescentMeters = 2,
                    FrontSpringRate = "50",
                    RearSpringRate = "60",
                    Updated = 99,
                    ClientUpdated = 88
                }
            ]
        });

        var session = await database.GetSessionAsync(sessionId);
        var changedSession = Assert.Single(await database.GetChangedAsync<Session>(0));
        var sessionTrack = await database.GetSessionTrackAsync(sessionId);
        var rawPsst = await database.GetSessionRawPsstAsync(sessionId);

        Assert.NotNull(session);
        Assert.Equal("remote", session!.Name);
        Assert.Equal("remote desc", session.Description);
        Assert.Equal(setupId, session.Setup);
        Assert.Equal(1234, session.Timestamp);
        Assert.Equal(trackId, session.FullTrack);
        Assert.Equal("""{"remote":true}""", session.ProcessingFingerprintJson);
        Assert.Equal(65, session.DurationSeconds);
        Assert.Equal(10, session.DistanceMeters);
        Assert.Equal(4, session.AscentMeters);
        Assert.Equal(2, session.DescentMeters);
        Assert.Equal("50", session.FrontSpringRate);
        Assert.Equal("60", session.RearSpringRate);
        Assert.True(session.HasProcessedData);
        Assert.True(changedSession.HasProcessedData);
        Assert.NotNull(sessionTrack);
        Assert.Equal(2, sessionTrack!.Count);
        Assert.Equal(originalPsst, rawPsst);

    }

    [Fact]
    public async Task MergeAllAsync_PushPayloadRoundTrip_PreservesExistingPsst_WhenSyncStopsBeforeRepair()
    {
        using var tempDatabase = new TempDatabase("interrupted-sync.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var originalPsst = new byte[] { 9, 8, 7, 6 };

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "server", "server desc", null, 50)
            {
                ProcessedData = originalPsst,
                Updated = 1,
                ClientUpdated = 1
            });
        }

        var pushPayload = AppJson.Deserialize<SynchronizationData>(AppJson.Serialize(new SynchronizationData
        {
            Sessions =
            [
                new Session(sessionId, "client", "client desc", null, 1234)
                {
                    ProcessedData = [1, 2, 3],
                    Updated = 99,
                    ClientUpdated = 88
                }
            ]
        }));

        Assert.NotNull(pushPayload);
        Assert.Single(pushPayload!.Sessions);
        Assert.Null(pushPayload.Sessions[0].ProcessedData);
        Assert.False(pushPayload.Sessions[0].HasProcessedData);

        await database.MergeAllAsync(pushPayload);

        Assert.Equal(originalPsst, await database.GetSessionRawPsstAsync(sessionId));
        Assert.Empty(await database.GetIncompleteSessionIdsAsync());

    }

    [Fact]
    public async Task MergeAllAsync_DoesNotMutateIncomingBoardTimestamps_WhenInsertOrUpdateAccepted()
    {
        using var tempDatabase = new TempDatabase("merge-board-nonmutating.db");
        var databasePath = tempDatabase.DatabasePath;
        var existingBoardId = Guid.NewGuid();
        var insertedBoardId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetAllAsync<Board>();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Board(existingBoardId, null)
            {
                Updated = 1,
                ClientUpdated = 1
            });
        }

        var updatedBoard = new Board(existingBoardId, Guid.NewGuid())
        {
            Updated = 99,
            ClientUpdated = 88
        };
        var insertedBoard = new Board(insertedBoardId, Guid.NewGuid())
        {
            Updated = 199,
            ClientUpdated = 177
        };

        await database.MergeAllAsync(new SynchronizationData
        {
            Boards = [updatedBoard, insertedBoard]
        });

        Assert.Equal(99, updatedBoard.Updated);
        Assert.Equal(88, updatedBoard.ClientUpdated);
        Assert.Equal(199, insertedBoard.Updated);
        Assert.Equal(177, insertedBoard.ClientUpdated);

        var storedUpdatedBoard = await database.GetAsync<Board>(existingBoardId);
        var storedInsertedBoard = await database.GetAsync<Board>(insertedBoardId);

        Assert.NotNull(storedUpdatedBoard);
        Assert.NotNull(storedInsertedBoard);
        Assert.Equal(99, storedUpdatedBoard!.ClientUpdated);
        Assert.Equal(199, storedInsertedBoard!.ClientUpdated);

    }

    [Fact]
    public async Task MergeAllAsync_DoesNotMutateIncomingSessionTimestamps_WhenInsertOrUpdateAccepted()
    {
        using var tempDatabase = new TempDatabase("merge-session-nonmutating.db");
        var databasePath = tempDatabase.DatabasePath;
        var existingSessionId = Guid.NewGuid();
        var insertedSessionId = Guid.NewGuid();
        var originalPsst = new byte[] { 4, 3, 2, 1 };

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(existingSessionId, "local", "local desc", null, 50)
            {
                ProcessedData = originalPsst,
                Updated = 1,
                ClientUpdated = 1
            });
        }

        var updatedSession = new Session(existingSessionId, "remote", "remote desc", null, 1234)
        {
            Updated = 99,
            ClientUpdated = 88
        };
        var insertedSession = new Session(insertedSessionId, "new", "new desc", null, 2345)
        {
            Updated = 199,
            ClientUpdated = 177
        };

        await database.MergeAllAsync(new SynchronizationData
        {
            Sessions = [updatedSession, insertedSession]
        });

        Assert.Equal(99, updatedSession.Updated);
        Assert.Equal(88, updatedSession.ClientUpdated);
        Assert.Equal(199, insertedSession.Updated);
        Assert.Equal(177, insertedSession.ClientUpdated);

        using var verificationConnection = new SQLiteConnection(databasePath);
        var storedUpdatedSession = verificationConnection.Table<Session>().Single(s => s.Id == existingSessionId);
        var storedInsertedSession = verificationConnection.Table<Session>().Single(s => s.Id == insertedSessionId);

        Assert.NotNull(storedUpdatedSession);
        Assert.NotNull(storedInsertedSession);
        Assert.Equal(99, storedUpdatedSession.ClientUpdated);
        Assert.Equal(199, storedInsertedSession.ClientUpdated);
        Assert.Equal(originalPsst, await database.GetSessionRawPsstAsync(existingSessionId));
        Assert.Null(await database.GetSessionRawPsstAsync(insertedSessionId));

    }

    [Fact]
    public async Task PutAsync_ReusesSoftDeletedBoardRow_InsteadOfInserting()
    {
        using var tempDatabase = new TempDatabase("board-revive.db");
        var databasePath = tempDatabase.DatabasePath;
        var boardId = Guid.NewGuid();
        var revivedSetupId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetAllAsync<Board>();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Board(boardId, null)
            {
                Updated = 10,
                ClientUpdated = 10,
                Deleted = 10
            });
        }

        await database.PutAsync(new Board(boardId, revivedSetupId));

        using var verificationConnection = new SQLiteConnection(databasePath);
        var boards = verificationConnection.Table<Board>().Where(b => b.Id == boardId).ToList();

        var board = Assert.Single(boards);
        Assert.Equal(revivedSetupId, board.SetupId);
        Assert.Null(board.Deleted);

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
    public async Task PatchSessionTrackAsync_UpdatesTrackAndBumpsSessionUpdated()
    {
        using var tempDatabase = new TempDatabase("session-track-patch.db");
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

    [Fact]
    public async Task AssociateSessionWithTrackAsync_UpdatesAssociationAndBumpsSessionUpdated()
    {
        using var tempDatabase = new TempDatabase("session-track-association.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid();
        var trackId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetSessionsAsync();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Session(sessionId, "session", "desc", null, 100)
            {
                Updated = 1,
                ClientUpdated = 1
            });
        }

        await database.PutAsync(new Track
        {
            Id = trackId,
            Points =
            [
                new TrackPoint(90, 1, 1, 0),
                new TrackPoint(110, 2, 2, 0)
            ]
        });

        var before = await database.GetSessionAsync(sessionId);

        var associatedTrackId = await database.AssociateSessionWithTrackAsync(sessionId);

        var after = await database.GetSessionAsync(sessionId);

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(trackId, associatedTrackId);
        Assert.Equal(trackId, after!.FullTrack);
        Assert.True(after.Updated > before!.Updated);

    }

    [Fact]
    public async Task MergeAllAsync_DoesNotResurrectDeletedBoard_WhenIncomingUpdateIsNotDeleted()
    {
        using var tempDatabase = new TempDatabase("merge-delete.db");
        var databasePath = tempDatabase.DatabasePath;
        var boardId = Guid.NewGuid();
        var deletedSetupId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetAllAsync<Board>();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Board(boardId, deletedSetupId)
            {
                Updated = 110,
                ClientUpdated = 100,
                Deleted = 100
            });
        }

        await database.MergeAllAsync(new SynchronizationData
        {
            Boards =
            [
                new Board(boardId, Guid.NewGuid())
                {
                    Updated = 150,
                    ClientUpdated = 150,
                    Deleted = null
                }
            ]
        });

        using var verificationConnection = new SQLiteConnection(databasePath);
        var board = verificationConnection.Table<Board>().Single(b => b.Id == boardId);

        Assert.Equal(deletedSetupId, board.SetupId);
        Assert.Equal(100, board.Deleted);

    }

    [Fact]
    public async Task MergeAllAsync_DoesNotApplyStaleDeleteOverNewerLiveBoard()
    {
        using var tempDatabase = new TempDatabase("merge-stale-delete.db");
        var databasePath = tempDatabase.DatabasePath;
        var boardId = Guid.NewGuid();
        var liveSetupId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetAllAsync<Board>();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Board(boardId, liveSetupId)
            {
                Updated = 210,
                ClientUpdated = 200
            });
        }

        await database.MergeAllAsync(new SynchronizationData
        {
            Boards =
            [
                new Board(boardId, null)
                {
                    Updated = 150,
                    ClientUpdated = 150,
                    Deleted = 150
                }
            ]
        });

        using var verificationConnection = new SQLiteConnection(databasePath);
        var board = verificationConnection.Table<Board>().Single(b => b.Id == boardId);

        Assert.Equal(liveSetupId, board.SetupId);
        Assert.Null(board.Deleted);

    }

    [Fact]
    public async Task MergeAllAsync_AppliesNewerDeleteOverOlderLiveBoard()
    {
        using var tempDatabase = new TempDatabase("merge-new-delete.db");
        var databasePath = tempDatabase.DatabasePath;
        var boardId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetAllAsync<Board>();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Board(boardId, Guid.NewGuid())
            {
                Updated = 110,
                ClientUpdated = 100
            });
        }

        await database.MergeAllAsync(new SynchronizationData
        {
            Boards =
            [
                new Board(boardId, null)
                {
                    Updated = 150,
                    ClientUpdated = 150,
                    Deleted = 150
                }
            ]
        });

        using var verificationConnection = new SQLiteConnection(databasePath);
        var board = verificationConnection.Table<Board>().Single(b => b.Id == boardId);

        Assert.Equal(150, board.Deleted);

    }

    [Fact]
    public async Task DeleteAsync_DoesNotRestampExistingTombstone()
    {
        using var tempDatabase = new TempDatabase("delete-tombstone.db");
        var databasePath = tempDatabase.DatabasePath;
        var boardId = Guid.NewGuid();

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetAllAsync<Board>();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Insert(new Board(boardId, null)
            {
                Updated = 10,
                ClientUpdated = 10,
                Deleted = 100
            });
        }

        await database.DeleteAsync(new Board(boardId, null));

        using var verificationConnection = new SQLiteConnection(databasePath);
        var board = verificationConnection.Table<Board>().Single(b => b.Id == boardId);

        Assert.Equal(100, board.Deleted);

    }

    private static RecordedSessionSource CreateRecordedSessionSource(Guid sessionId) => new()
    {
        SessionId = sessionId,
        SourceKind = RecordedSessionSourceKind.ImportedSst,
        SourceName = "source.SST",
        SchemaVersion = 1,
        SourceHash = RecordedSessionSourceHash.Compute(
            RecordedSessionSourceKind.ImportedSst,
            "source.SST",
            1,
            [1, 2, 3]),
        Payload = [1, 2, 3]
    };

    private static Track CreateFullTrack() => new()
    {
        Id = Guid.NewGuid(),
        Points =
        [
            new TrackPoint(100, 1, 1, 10),
            new TrackPoint(101, 2, 2, 11)
        ]
    };

    private static byte[] CreateTelemetryBlob(double durationSeconds) => new TelemetryData
    {
        Metadata = new Metadata
        {
            SourceName = "source.SST",
            Version = 4,
            SampleRate = 100,
            Timestamp = 100,
            Duration = durationSeconds
        }
    }.BinaryForm;

    private static SqliteConnectionContext CreateConnectionContext(
        string databasePath,
        IEnumerable<IExtensionDatabaseMigrator> extensionMigrators) =>
        new(
            databasePath,
            createAppDirectories: false,
            extensionMigrators,
            extensionCascadeRuleProviders: [],
            extensionStateRefreshParticipantsProvider: () => []);

    private sealed class TestPersistenceHarness
    {
        private readonly SqliteConnectionContext context;
        private readonly ITrackRepository trackRepository;
        private readonly ISessionRepository sessionRepository;
        private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository;
        private readonly ISyncDataStore syncDataStore;
        private readonly IExtensionDatabaseConnection extensionDatabaseConnection;

        public TestPersistenceHarness(string databasePath)
            : this(CreateConnectionContext(databasePath, []))
        {
        }

        public TestPersistenceHarness(
            string databasePath,
            IEnumerable<IExtensionDatabaseMigrator> extensionMigrators)
            : this(CreateConnectionContext(databasePath, extensionMigrators))
        {
        }

        private TestPersistenceHarness(SqliteConnectionContext context)
        {
            this.context = context;
            trackRepository = new TrackRepository(context);
            sessionRepository = new SessionRepository(context, new SessionTelemetryProcessor(), trackRepository);
            recordedSessionSourceRepository = new RecordedSessionSourceRepository(context);
            syncDataStore = new SynchronizationMergeEngine(context, trackRepository);
            extensionDatabaseConnection = new ExtensionDatabaseConnection(context);
        }

        public Task<SQLiteAsyncConnection> GetInitializedConnectionAsync() =>
            context.GetInitializedConnectionAsync();

        public Task<IExtensionDatabaseSession> OpenSessionAsync() =>
            extensionDatabaseConnection.OpenSessionAsync();

        public Task<List<T>> GetAllAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>()
            where T : Synchronizable, new() =>
            new SynchronizableRepository<T>(context).GetAllAsync();

        public async Task<List<T>> GetChangedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(long since)
            where T : Synchronizable, new()
        {
            if (typeof(T) == typeof(Session))
            {
                var synchronizationData = await syncDataStore.GetSynchronizationDataAsync(since);
                return (List<T>)(object)synchronizationData.Sessions;
            }

            return await new SynchronizableRepository<T>(context).GetChangedAsync(since);
        }

        public Task<T?> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(Guid id)
            where T : Synchronizable, new() =>
            new SynchronizableRepository<T>(context).GetAsync(id);

        public Task<Guid> PutAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T item)
            where T : Synchronizable, new() =>
            new SynchronizableRepository<T>(context).PutAsync(item);

        public Task DeleteAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T item)
            where T : Synchronizable, new() =>
            new SynchronizableRepository<T>(context).DeleteAsync(item);

        public Task<List<Session>> GetSessionsAsync() =>
            sessionRepository.GetSessionsAsync();

        public Task<Session?> GetSessionAsync(Guid id) =>
            sessionRepository.GetSessionAsync(id);

        public Task<List<Guid>> GetIncompleteSessionIdsAsync() =>
            sessionRepository.GetIncompleteSessionIdsAsync();

        public Task<TelemetryData?> GetSessionPsstAsync(Guid id) =>
            sessionRepository.GetSessionPsstAsync(id);

        public Task<byte[]?> GetSessionRawPsstAsync(Guid id) =>
            sessionRepository.GetSessionRawPsstAsync(id);

        public Task<List<TrackPoint>?> GetSessionTrackAsync(Guid id) =>
            sessionRepository.GetSessionTrackAsync(id);

        public Task<Guid> PutSessionAsync(Session session) =>
            sessionRepository.PutSessionAsync(session);

        public Task<Session> PutProcessedSessionAsync(
            Session session,
            Track? newFullTrack,
            RecordedSessionSource? source) =>
            sessionRepository.PutProcessedSessionAsync(session, newFullTrack, source);

        public Task<Session?> PutProcessedSessionIfUnchangedAsync(
            Session session,
            Track? newFullTrack,
            RecordedSessionSource? source,
            long baselineUpdated) =>
            sessionRepository.PutProcessedSessionIfUnchangedAsync(session, newFullTrack, source, baselineUpdated);

        public Task PatchSessionPsstAsync(Guid id, byte[] data) =>
            sessionRepository.PatchSessionPsstAsync(id, data);

        public Task PatchSessionTrackAsync(Guid id, List<TrackPoint> points) =>
            sessionRepository.PatchSessionTrackAsync(id, points);

        public Task<List<RecordedSessionSource>> GetRecordedSessionSourcesAsync() =>
            recordedSessionSourceRepository.GetRecordedSessionSourcesAsync();

        public Task<RecordedSessionSource?> GetRecordedSessionSourceAsync(Guid id) =>
            recordedSessionSourceRepository.GetRecordedSessionSourceAsync(id);

        public Task<List<Guid>> GetSessionIdsMissingRecordedSourceAsync() =>
            recordedSessionSourceRepository.GetSessionIdsMissingRecordedSourceAsync();

        public Task PutRecordedSessionSourceAsync(RecordedSessionSource source) =>
            recordedSessionSourceRepository.PutRecordedSessionSourceAsync(source);

        public Task DeleteRecordedSessionSourceAsync(Guid sessionId) =>
            recordedSessionSourceRepository.DeleteRecordedSessionSourceAsync(sessionId);

        public Task<Guid?> FindTrackByTimeRangeAsync(long startTime, long endTime) =>
            trackRepository.FindTrackByTimeRangeAsync(startTime, endTime);

        public Task<Guid?> AssociateSessionWithTrackAsync(Guid sessionId) =>
            trackRepository.AssociateSessionWithTrackAsync(sessionId);

        public Task<long> GetLastSyncTimeAsync(string? serverUrl) =>
            syncDataStore.GetLastSyncTimeAsync(serverUrl);

        public Task UpdateLastSyncTimeAsync(string? serverUrl) =>
            syncDataStore.UpdateLastSyncTimeAsync(serverUrl);

        public Task<SynchronizationData> GetSynchronizationDataAsync(long since) =>
            syncDataStore.GetSynchronizationDataAsync(since);

        public Task ApplyRemoteSynchronizationDataAsync(SynchronizationData data) =>
            syncDataStore.ApplyRemoteSynchronizationDataAsync(data);

        public Task MergeAllAsync(SynchronizationData data) =>
            syncDataStore.MergeAllAsync(data);
    }

    private sealed class TableColumnInfo
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class SqliteMasterRow
    {
        public string Name { get; set; } = string.Empty;
    }

    [Table("test_extension_row")]
    private sealed class TestExtensionRow
    {
        [PrimaryKey]
        [Column("id")]
        public string Id { get; set; } = string.Empty;

        [Column("value")]
        public int Value { get; set; }
    }

    [Table("second_test_extension_row")]
    private sealed class SecondTestExtensionRow
    {
        [PrimaryKey]
        [Column("id")]
        public string Id { get; set; } = string.Empty;
    }

    [Table("test_extension_row")]
    private sealed class DuplicateNamedExtensionRow
    {
        [PrimaryKey]
        [Column("id")]
        public string Id { get; set; } = string.Empty;
    }

    [Table("session")]
    private sealed class CoreNamedExtensionRow
    {
        [PrimaryKey]
        [Column("id")]
        public string Id { get; set; } = string.Empty;
    }

    private sealed class TestExtensionMigrator(
        string extensionId,
        int targetVersion,
        IReadOnlyList<Type> tableTypes,
        IReadOnlyList<ExtensionDatabaseMigrationStep> steps) : IExtensionDatabaseMigrator
    {
        public string ExtensionId { get; } = extensionId;
        public int TargetVersion { get; } = targetVersion;
        public IReadOnlyList<Type> TableTypes { get; } = tableTypes;
        public IReadOnlyList<ExtensionDatabaseMigrationStep> Steps { get; } = steps;
    }
}
