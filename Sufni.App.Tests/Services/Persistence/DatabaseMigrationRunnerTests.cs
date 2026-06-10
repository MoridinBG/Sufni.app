using SQLite;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.ExtensionHost.Models;
using Sufni.App.ExtensionHost.SessionDetails;
using Sufni.App.ExtensionHosting.Database;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Services.Persistence;

public class DatabaseMigrationRunnerTests
{
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
        var context = PersistenceTestData.CreateConnectionContext(databasePath, [migrator]);

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

        var firstRun = PersistenceTestData.CreateConnectionContext(databasePath, [migrator]);
        _ = await firstRun.GetInitializedConnectionAsync();

        var secondRun = PersistenceTestData.CreateConnectionContext(databasePath, [migrator]);
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
}
