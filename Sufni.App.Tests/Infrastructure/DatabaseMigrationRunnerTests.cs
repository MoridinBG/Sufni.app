using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Kinematics;
using Sufni.Telemetry;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Setups.Models;
using Sufni.App.Extensibility.Database;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Infrastructure;

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
                TestSnapshots.FullSuspensionLinkageSpec().ToJson(),
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

            Assert.Contains(columns, column => column.Name == "rear_suspension");
            Assert.DoesNotContain(columns, column => column.Name == "rear_suspension_kind");
            Assert.DoesNotContain(columns, column => column.Name == "linkage");
            Assert.DoesNotContain(columns, column => column.Name == "leverage_ratio");
            Assert.Contains(columns, column => column.Name == "front_compression_damping_cutoff_mm_per_second");
            Assert.Contains(columns, column => column.Name == "front_rebound_damping_cutoff_mm_per_second");
            Assert.Contains(columns, column => column.Name == "rear_compression_damping_cutoff_mm_per_second");
            Assert.Contains(columns, column => column.Name == "rear_rebound_damping_cutoff_mm_per_second");

            var rearSuspensionJson = verificationConnection.ExecuteScalar<string>(
                "SELECT rear_suspension FROM bike WHERE id = ?",
                legacyBikeId.ToString());
            var rearSuspension = RearSuspensionJsonCodec.Deserialize(rearSuspensionJson);
            var linkage = Assert.IsType<RearSuspensionSpec.Linkage>(rearSuspension);
            Assert.Equal(0.5, linkage.Spec.ShockStroke);
        }

        var firstRunBike = Assert.Single(firstRunBikes);
        Assert.Equal(legacyBikeId, firstRunBike.Id);
        var firstRunLinkage = Assert.IsType<RearSuspensionSpec.Linkage>(firstRunBike.RearSuspension);
        Assert.Equal(0.5, firstRunLinkage.Spec.ShockStroke);
        Assert.Equal(DampingSpeedCutoffs.DefaultMmPerSecond, firstRunBike.FrontCompressionDampingCutoffMmPerSecond);
        Assert.Equal(DampingSpeedCutoffs.DefaultMmPerSecond, firstRunBike.FrontReboundDampingCutoffMmPerSecond);
        Assert.Equal(DampingSpeedCutoffs.DefaultMmPerSecond, firstRunBike.RearCompressionDampingCutoffMmPerSecond);
        Assert.Equal(DampingSpeedCutoffs.DefaultMmPerSecond, firstRunBike.RearReboundDampingCutoffMmPerSecond);

        var secondRun = new TestPersistenceHarness(databasePath);
        var secondRunBikes = await secondRun.GetAllAsync<Bike>();

        var secondRunBike = Assert.Single(secondRunBikes);
        Assert.Equal(legacyBikeId, secondRunBike.Id);
        var secondRunLinkage = Assert.IsType<RearSuspensionSpec.Linkage>(secondRunBike.RearSuspension);
        Assert.Equal(0.5, secondRunLinkage.Spec.ShockStroke);
        Assert.Equal(DampingSpeedCutoffs.DefaultMmPerSecond, secondRunBike.FrontCompressionDampingCutoffMmPerSecond);
        Assert.Equal(DampingSpeedCutoffs.DefaultMmPerSecond, secondRunBike.FrontReboundDampingCutoffMmPerSecond);
        Assert.Equal(DampingSpeedCutoffs.DefaultMmPerSecond, secondRunBike.RearCompressionDampingCutoffMmPerSecond);
        Assert.Equal(DampingSpeedCutoffs.DefaultMmPerSecond, secondRunBike.RearReboundDampingCutoffMmPerSecond);

    }

    [Fact]
    public async Task Initialization_BackfillsLegacyRearSuspensionMatrix()
    {
        using var tempDatabase = new TempDatabase("legacy-rear-suspension-matrix.db");
        var databasePath = tempDatabase.DatabasePath;
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25));
        var cases = new[]
        {
            LegacyRearSuspensionCase.Create(
                "linkage kind with linkage payload and shock column",
                (int)RearSuspensionKind.Linkage,
                linkage.ToJson(),
                leverageRatioJson: null,
                shockStroke: 0.6,
                new RearSuspensionSpec.Linkage(linkage.WithShockStroke(0.6)),
                expectedShockStroke: 0.6),
            LegacyRearSuspensionCase.Create(
                "linkage kind with linkage payload and null shock column",
                (int)RearSuspensionKind.Linkage,
                linkage.ToJson(),
                leverageRatioJson: null,
                shockStroke: null,
                new RearSuspensionSpec.Linkage(linkage),
                expectedShockStroke: linkage.ShockStroke),
            LegacyRearSuspensionCase.Create(
                "linkage kind with missing linkage payload",
                (int)RearSuspensionKind.Linkage,
                linkageJson: null,
                leverageRatio.ToJson(),
                shockStroke: null,
                new RearSuspensionSpec.LinkageDraft(),
                expectedShockStroke: null),
            LegacyRearSuspensionCase.Create(
                "linkage kind with invalid linkage payload",
                (int)RearSuspensionKind.Linkage,
                linkageJson: "{",
                leverageRatio.ToJson(),
                shockStroke: 0.8,
                new RearSuspensionSpec.LinkageDraft(),
                expectedShockStroke: 0.8),
            LegacyRearSuspensionCase.Create(
                "leverage-ratio kind with leverage-ratio payload",
                (int)RearSuspensionKind.LeverageRatio,
                linkageJson: null,
                leverageRatio.ToJson(),
                shockStroke: 10,
                new RearSuspensionSpec.LeverageRatio(leverageRatio),
                expectedShockStroke: 10),
            LegacyRearSuspensionCase.Create(
                "leverage-ratio kind with missing leverage-ratio payload",
                (int)RearSuspensionKind.LeverageRatio,
                linkage.ToJson(),
                leverageRatioJson: null,
                shockStroke: 12,
                new RearSuspensionSpec.LeverageRatioDraft(),
                expectedShockStroke: 12),
            LegacyRearSuspensionCase.Create(
                "leverage-ratio kind with invalid leverage-ratio payload",
                (int)RearSuspensionKind.LeverageRatio,
                linkage.ToJson(),
                leverageRatioJson: "{",
                shockStroke: 11,
                new RearSuspensionSpec.LeverageRatioDraft(),
                expectedShockStroke: 11),
            LegacyRearSuspensionCase.Create(
                "none kind with orphan linkage payload",
                (int)RearSuspensionKind.None,
                linkage.ToJson(),
                leverageRatioJson: null,
                shockStroke: 0.7,
                new RearSuspensionSpec.Linkage(linkage.WithShockStroke(0.7)),
                expectedShockStroke: 0.7),
            LegacyRearSuspensionCase.Create(
                "null kind with orphan leverage-ratio payload",
                rearSuspensionKind: null,
                linkageJson: null,
                leverageRatio.ToJson(),
                shockStroke: null,
                new RearSuspensionSpec.LeverageRatio(leverageRatio),
                expectedShockStroke: null),
            LegacyRearSuspensionCase.Create(
                "invalid kind with linkage payload",
                99,
                linkage.ToJson(),
                leverageRatioJson: null,
                shockStroke: null,
                new RearSuspensionSpec.Linkage(linkage),
                expectedShockStroke: linkage.ShockStroke),
            LegacyRearSuspensionCase.Create(
                "null kind with both payloads prefers linkage",
                rearSuspensionKind: null,
                linkage.ToJson(),
                leverageRatio.ToJson(),
                shockStroke: null,
                new RearSuspensionSpec.Linkage(linkage),
                expectedShockStroke: linkage.ShockStroke),
            LegacyRearSuspensionCase.Create(
                "null kind with invalid payloads",
                rearSuspensionKind: null,
                linkageJson: "{",
                leverageRatioJson: "{",
                shockStroke: null,
                new RearSuspensionSpec.Hardtail(),
                expectedShockStroke: null),
        };

        using (var connection = new SQLiteConnection(databasePath))
        {
            CreateLegacyBikeTableWithRearSuspensionColumns(connection);
            foreach (var testCase in cases)
            {
                InsertLegacyBike(connection, testCase);
            }
        }

        var database = new TestPersistenceHarness(databasePath);
        var bikes = (await database.GetAllAsync<Bike>()).ToDictionary(bike => bike.Id);
        Assert.Equal(cases.Length, bikes.Count);

        using (var verificationConnection = new SQLiteConnection(databasePath))
        {
            var columns = verificationConnection.Query<TableColumnInfo>("PRAGMA table_info(bike)");
            Assert.DoesNotContain(columns, column => column.Name == "rear_suspension_kind");
            Assert.DoesNotContain(columns, column => column.Name == "linkage");
            Assert.DoesNotContain(columns, column => column.Name == "leverage_ratio");
        }

        foreach (var testCase in cases)
        {
            var bike = bikes[testCase.Id];

            Assert.Equal(testCase.ExpectedRearSuspension, bike.RearSuspension);
            Assert.Equal(testCase.ExpectedShockStroke, bike.ShockStroke);
        }
    }

    [Fact]
    public async Task NewBikeRows_WriteRearSuspensionColumn()
    {
        using var tempDatabase = new TempDatabase("new-bike-rear-suspension.db");
        var databasePath = tempDatabase.DatabasePath;
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var bike = new Bike(Guid.NewGuid(), "new linkage bike")
        {
            HeadAngle = 64,
            ForkStroke = 150,
            ShockStroke = linkage.ShockStroke,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage),
            Updated = 1,
            ClientUpdated = 1,
        };
        var database = new TestPersistenceHarness(databasePath);

        await database.PutAsync(bike);

        using var connection = new SQLiteConnection(databasePath);
        var columns = connection.Query<TableColumnInfo>("PRAGMA table_info(bike)");
        Assert.Contains(columns, column => column.Name == "rear_suspension");
        Assert.DoesNotContain(columns, column => column.Name == "rear_suspension_kind");
        Assert.DoesNotContain(columns, column => column.Name == "linkage");
        Assert.DoesNotContain(columns, column => column.Name == "leverage_ratio");

        var rearSuspensionJson = connection.ExecuteScalar<string>(
            "SELECT rear_suspension FROM bike WHERE id = ?",
            bike.Id.ToString());
        var persistedRearSuspension = RearSuspensionJsonCodec.Deserialize(rearSuspensionJson);

        var persistedLinkage = Assert.IsType<RearSuspensionSpec.Linkage>(persistedRearSuspension);
        Assert.Equal(linkage, persistedLinkage.Spec);
    }

    [Fact]
    public async Task Initialization_LeavesAlreadyMigratedRearSuspensionRowsUnchanged()
    {
        using var tempDatabase = new TempDatabase("already-migrated-rear-suspension.db");
        var databasePath = tempDatabase.DatabasePath;
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var bike = new Bike(Guid.NewGuid(), "already migrated linkage bike")
        {
            HeadAngle = 64,
            ForkStroke = 150,
            ShockStroke = linkage.ShockStroke,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage),
            Updated = 1,
            ClientUpdated = 1,
        };
        var firstRun = new TestPersistenceHarness(databasePath);
        await firstRun.PutAsync(bike);

        string beforeJson;
        using (var connection = new SQLiteConnection(databasePath))
        {
            beforeJson = connection.ExecuteScalar<string>(
                "SELECT rear_suspension FROM bike WHERE id = ?",
                bike.Id.ToString());
        }

        var secondRun = new TestPersistenceHarness(databasePath);
        var bikes = await secondRun.GetAllAsync<Bike>();

        using (var connection = new SQLiteConnection(databasePath))
        {
            var columns = connection.Query<TableColumnInfo>("PRAGMA table_info(bike)");
            Assert.DoesNotContain(columns, column => column.Name == "rear_suspension_kind");
            Assert.DoesNotContain(columns, column => column.Name == "linkage");
            Assert.DoesNotContain(columns, column => column.Name == "leverage_ratio");

            var afterJson = connection.ExecuteScalar<string>(
                "SELECT rear_suspension FROM bike WHERE id = ?",
                bike.Id.ToString());
            Assert.Equal(beforeJson, afterJson);
        }

        var persistedBike = Assert.Single(bikes);
        Assert.Equal(bike.RearSuspension, persistedBike.RearSuspension);
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
    public async Task Initialization_DropsLegacySessionCacheTable_AndDoesNotRecreateIt()
    {
        using var tempDatabase = new TempDatabase("legacy-session-cache.db");
        var databasePath = tempDatabase.DatabasePath;
        var sessionId = Guid.NewGuid().ToString();

        using (var seedConnection = new SQLiteConnection(databasePath))
        {
            seedConnection.Execute(
                """
                CREATE TABLE session_cache (
                    session_id TEXT PRIMARY KEY,
                    compact_signal_rows TEXT NOT NULL
                )
                """);
            seedConnection.Execute(
                "INSERT INTO session_cache (session_id, compact_signal_rows) VALUES (?, ?)",
                sessionId,
                "[]");
        }

        var firstRun = new TestPersistenceHarness(databasePath);
        _ = await firstRun.GetSessionsAsync();

        using (var firstVerification = new SQLiteConnection(databasePath))
        {
            var tables = firstVerification.Query<SqliteMasterRow>(
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'session_cache'");
            Assert.Empty(tables);
        }

        var secondRun = new TestPersistenceHarness(databasePath);
        _ = await secondRun.GetSessionsAsync();

        using var secondVerification = new SQLiteConnection(databasePath);
        var recreatedTables = secondVerification.Query<SqliteMasterRow>(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'session_cache'");
        Assert.Empty(recreatedTables);
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
    public async Task Initialization_DoesNotBackfillLegacySessionProcessingFingerprintAfterProcessingVersionAdvance()
    {
        using var tempDatabase = new TempDatabase("legacy-session-fingerprint-backfill.db");
        var seed = SeedProcessedSessionDatabase(tempDatabase.DatabasePath);

        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var persisted = await database.GetSessionAsync(seed.Session.Id);

        Assert.NotNull(persisted);
        Assert.Equal(seed.Session.Updated, persisted.Updated);
        Assert.Equal(seed.ProcessedData, await database.GetSessionRawPsstAsync(seed.Session.Id));

        var fingerprintService = new ProcessingFingerprintService();
        var evaluation = fingerprintService.EvaluateState(
            SessionSnapshot.From(persisted),
            SetupSnapshot.From(seed.Setup, boardId: null),
            BikeSnapshot.From(seed.Bike),
            RecordedSessionSourceSnapshot.From(seed.Source));

        Assert.Null(evaluation.Persisted);
        Assert.IsType<SessionStaleness.UnknownLegacyFingerprint>(evaluation.Staleness);
    }

    [Fact]
    public async Task Initialization_DoesNotBackfillPreviousProcessingVersionFingerprintAfterProcessingVersionAdvance()
    {
        using var tempDatabase = new TempDatabase("old-processing-version-fingerprint.db");
        ProcessingFingerprint? staleFingerprint = null;
        var seed = SeedProcessedSessionDatabase(
            tempDatabase.DatabasePath,
            seed =>
            {
                var current = CreateCurrentFingerprint(seed);
                staleFingerprint = current with
                {
                    ProcessingVersion = TelemetryProcessingVersion.Current - 1
                };
                return AppJson.Serialize(staleFingerprint);
            });

        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var persisted = await database.GetSessionAsync(seed.Session.Id);

        Assert.NotNull(persisted);
        Assert.Equal(seed.Session.Updated, persisted.Updated);
        Assert.Equal(seed.ProcessedData, await database.GetSessionRawPsstAsync(seed.Session.Id));

        var fingerprintService = new ProcessingFingerprintService();
        var evaluation = fingerprintService.EvaluateState(
            SessionSnapshot.From(persisted),
            SetupSnapshot.From(seed.Setup, boardId: null),
            BikeSnapshot.From(seed.Bike),
            RecordedSessionSourceSnapshot.From(seed.Source));

        Assert.Equal(staleFingerprint, evaluation.Persisted);
        var staleness = Assert.IsType<SessionStaleness.ProcessingVersionChanged>(evaluation.Staleness);
        Assert.Equal(TelemetryProcessingVersion.Current - 1, staleness.Persisted);
        Assert.Equal(TelemetryProcessingVersion.Current, staleness.CurrentVersion);
    }

    [Fact]
    public async Task Initialization_DoesNotBackfillLegacyRearSuspensionKindFingerprintAfterProcessingVersionAdvance()
    {
        using var tempDatabase = new TempDatabase("legacy-rear-suspension-kind-fingerprint.db");
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var bikeSnapshot = TestSnapshots.Bike(id: Guid.NewGuid(), updated: 20) with
        {
            ShockStroke = linkage.ShockStroke,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage)
        };
        ProcessingFingerprint? staleFingerprint = null;
        var seed = SeedProcessedSessionDatabase(
            tempDatabase.DatabasePath,
            seed =>
            {
                var fingerprintService = new ProcessingFingerprintService();
                var legacyBike = BikeSnapshot.From(seed.Bike) with
                {
                    RearSuspension = new RearSuspensionSpec.Hardtail()
                };
                staleFingerprint = fingerprintService.CreateCurrent(
                    SessionSnapshot.From(seed.Session),
                    SetupSnapshot.From(seed.Setup, boardId: null),
                    legacyBike,
                    RecordedSessionSourceSnapshot.From(seed.Source));
                return AppJson.Serialize(staleFingerprint);
            },
            bikeSnapshot,
            storeLegacyRearSuspensionKind: true);

        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var persisted = await database.GetSessionAsync(seed.Session.Id);
        var persistedBike = await database.GetAsync<Bike>(seed.Bike.Id);

        Assert.NotNull(persisted);
        Assert.NotNull(persistedBike);
        Assert.IsType<RearSuspensionSpec.Linkage>(persistedBike.RearSuspension);
        Assert.Equal(seed.Session.Updated, persisted.Updated);
        Assert.Equal(seed.ProcessedData, await database.GetSessionRawPsstAsync(seed.Session.Id));

        var fingerprintService = new ProcessingFingerprintService();
        var evaluation = fingerprintService.EvaluateState(
            SessionSnapshot.From(persisted),
            SetupSnapshot.From(seed.Setup, boardId: null),
            BikeSnapshot.From(persistedBike),
            RecordedSessionSourceSnapshot.From(seed.Source));

        Assert.Equal(staleFingerprint, evaluation.Persisted);
        Assert.IsType<SessionStaleness.DependencyHashChanged>(evaluation.Staleness);
    }

    [Fact]
    public async Task Initialization_DoesNotBackfillExistingDependencyHashMismatchAfterProcessingVersionAdvance()
    {
        using var tempDatabase = new TempDatabase("existing-dependency-mismatch-fingerprint.db");
        ProcessingFingerprint? staleFingerprint = null;
        var seed = SeedProcessedSessionDatabase(
            tempDatabase.DatabasePath,
            seed =>
            {
                staleFingerprint = CreateCurrentFingerprint(seed) with
                {
                    DependencyHash = "changed-dependency"
                };
                return AppJson.Serialize(staleFingerprint);
            });

        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var persisted = await database.GetSessionAsync(seed.Session.Id);

        Assert.NotNull(persisted);
        Assert.Equal(seed.Session.Updated, persisted.Updated);
        Assert.Equal(seed.ProcessedData, await database.GetSessionRawPsstAsync(seed.Session.Id));

        var fingerprintService = new ProcessingFingerprintService();
        var evaluation = fingerprintService.EvaluateState(
            SessionSnapshot.From(persisted),
            SetupSnapshot.From(seed.Setup, boardId: null),
            BikeSnapshot.From(seed.Bike),
            RecordedSessionSourceSnapshot.From(seed.Source));

        Assert.Equal(staleFingerprint, evaluation.Persisted);
        Assert.IsType<SessionStaleness.DependencyHashChanged>(evaluation.Staleness);
    }

    [Fact]
    public async Task Initialization_DoesNotBackfillUnexpectedProcessingVersionFingerprint()
    {
        using var tempDatabase = new TempDatabase("unexpected-processing-version-fingerprint.db");
        ProcessingFingerprint? staleFingerprint = null;
        var seed = SeedProcessedSessionDatabase(
            tempDatabase.DatabasePath,
            seed =>
            {
                staleFingerprint = CreateCurrentFingerprint(seed) with
                {
                    ProcessingVersion = 0
                };
                return AppJson.Serialize(staleFingerprint);
            });

        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var persisted = await database.GetSessionAsync(seed.Session.Id);

        Assert.NotNull(persisted);
        var fingerprintService = new ProcessingFingerprintService();
        var evaluation = fingerprintService.EvaluateState(
            SessionSnapshot.From(persisted),
            SetupSnapshot.From(seed.Setup, boardId: null),
            BikeSnapshot.From(seed.Bike),
            RecordedSessionSourceSnapshot.From(seed.Source));

        Assert.Equal(staleFingerprint, evaluation.Persisted);
        Assert.IsType<SessionStaleness.ProcessingVersionChanged>(evaluation.Staleness);
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

    private static SeededProcessedSession SeedProcessedSessionDatabase(
        string databasePath,
        Func<SeededProcessedSession, string?>? createProcessingFingerprintJson = null,
        BikeSnapshot? bikeSnapshot = null,
        bool storeLegacyRearSuspensionKind = false)
    {
        var bike = Bike.FromSnapshot(bikeSnapshot ?? TestSnapshots.Bike(id: Guid.NewGuid(), updated: 20));
        var setup = new Setup(Guid.NewGuid(), "seed setup")
        {
            BikeId = bike.Id,
            Updated = 21,
            ClientUpdated = 21
        };
        var processedData = PersistenceTestData.CreateTelemetryBlob(12.5);
        var session = new Session(Guid.NewGuid(), "seed session", string.Empty, setup.Id, timestamp: 100)
        {
            ProcessedData = processedData,
            Updated = 22,
            ClientUpdated = 22
        };
        var source = PersistenceTestData.CreateRecordedSessionSource(session.Id);
        var seed = new SeededProcessedSession(bike, setup, session, source, processedData);
        session.ProcessingFingerprintJson = createProcessingFingerprintJson?.Invoke(seed);

        using var connection = new SQLiteConnection(databasePath);
        connection.CreateTable<Bike>();
        connection.CreateTable<Setup>();
        connection.CreateTable<Session>();
        connection.CreateTable<RecordedSessionSource>();

        connection.Insert(bike);
        connection.Insert(setup);
        connection.Insert(session);
        connection.Insert(source);
        if (storeLegacyRearSuspensionKind)
        {
            connection.Execute("ALTER TABLE bike ADD COLUMN rear_suspension_kind INTEGER");
            connection.Execute("ALTER TABLE bike ADD COLUMN linkage TEXT");
            connection.Execute("ALTER TABLE bike ADD COLUMN leverage_ratio TEXT");
            var linkageJson = bike.RearSuspension is RearSuspensionSpec.Linkage linkage
                ? linkage.Spec.ToJson()
                : null;
            connection.Execute(
                "UPDATE bike SET rear_suspension_kind = ?, linkage = ? WHERE id = ?",
                (int)RearSuspensionKind.None,
                linkageJson,
                bike.Id);
        }

        return seed;
    }

    private static void CreateLegacyBikeTableWithRearSuspensionColumns(SQLiteConnection connection)
    {
        connection.Execute(
            """
            CREATE TABLE bike (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                head_angle REAL NOT NULL,
                fork_stroke REAL,
                shock_stroke REAL,
                rear_suspension_kind INTEGER,
                linkage TEXT,
                leverage_ratio TEXT,
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
    }

    private static void InsertLegacyBike(SQLiteConnection connection, LegacyRearSuspensionCase testCase)
    {
        connection.Execute(
            """
            INSERT INTO bike (
                id,
                name,
                head_angle,
                fork_stroke,
                shock_stroke,
                rear_suspension_kind,
                linkage,
                leverage_ratio,
                pixels_to_millimeters,
                image_rotation_degrees,
                image,
                updated)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            testCase.Id.ToString(),
            testCase.Name,
            64.0,
            150.0,
            testCase.ShockStroke,
            testCase.RearSuspensionKind,
            testCase.LinkageJson,
            testCase.LeverageRatioJson,
            0.0,
            0.0,
            Array.Empty<byte>(),
            1);
    }

    private static ProcessingFingerprint CreateCurrentFingerprint(SeededProcessedSession seed)
    {
        var service = new ProcessingFingerprintService();
        return service.CreateCurrent(
            SessionSnapshot.From(seed.Session),
            SetupSnapshot.From(seed.Setup, boardId: null),
            BikeSnapshot.From(seed.Bike),
            RecordedSessionSourceSnapshot.From(seed.Source));
    }

    private sealed record SeededProcessedSession(
        Bike Bike,
        Setup Setup,
        Session Session,
        RecordedSessionSource Source,
        byte[] ProcessedData);

    private sealed record LegacyRearSuspensionCase(
        Guid Id,
        string Name,
        int? RearSuspensionKind,
        string? LinkageJson,
        string? LeverageRatioJson,
        double? ShockStroke,
        RearSuspensionSpec ExpectedRearSuspension,
        double? ExpectedShockStroke)
    {
        public static LegacyRearSuspensionCase Create(
            string name,
            int? rearSuspensionKind,
            string? linkageJson,
            string? leverageRatioJson,
            double? shockStroke,
            RearSuspensionSpec expectedRearSuspension,
            double? expectedShockStroke) => new(
            Guid.NewGuid(),
            name,
            rearSuspensionKind,
            linkageJson,
            leverageRatioJson,
            shockStroke,
            expectedRearSuspension,
            expectedShockStroke);
    }
}
