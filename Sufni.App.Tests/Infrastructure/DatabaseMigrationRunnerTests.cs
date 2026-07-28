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
using Sufni.App.Sessions.Services;
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
    public enum ExtensionMigratorValidationCase
    {
        DuplicateIds,
        DuplicateTableOwnership,
        ReservedCoreTableName,
    }

    public enum ProcessedSessionFingerprintCase
    {
        MissingLegacyFingerprint,
        PreviousProcessingVersion,
        LegacyRearSuspensionKind,
        DependencyHashMismatch,
        UnexpectedProcessingVersion,
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
                "none kind without payloads",
                (int)RearSuspensionKind.None,
                linkageJson: null,
                leverageRatioJson: null,
                shockStroke: null,
                new RearSuspensionSpec.Hardtail(),
                expectedShockStroke: null),
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
    public async Task Initialization_BackfillsLocalContentRevisions_AndIsIdempotent()
    {
        using var tempDatabase = new TempDatabase("local-content-revisions.db");
        var sessionId = Guid.NewGuid();
        var trackId = Guid.NewGuid();

        using (var connection = new SQLiteConnection(tempDatabase.DatabasePath))
        {
            connection.CreateTable<Session>();
            connection.CreateTable<Track>();
            connection.Insert(new Session(sessionId, "session", string.Empty, null, 100)
            {
                ProcessedData = [1, 2, 3],
                ProcessingFingerprintJson = "fingerprint",
                Track = [new TrackPoint(100, 1, 2, 3)],
                DurationSeconds = 1,
                FullTrack = trackId,
                Updated = 10,
            });
            connection.Insert(new Track
            {
                Id = trackId,
                Points = [new TrackPoint(100, 1, 2, 3)],
                Updated = 11,
            });
            connection.Execute("UPDATE session SET processed_telemetry_revision = 0, track_projection_revision = 0");
            connection.Execute("UPDATE track SET points_revision = 0");
        }

        var firstRun = new TestPersistenceHarness(tempDatabase.DatabasePath);
        _ = await firstRun.GetInitializedConnectionAsync();
        var secondRun = new TestPersistenceHarness(tempDatabase.DatabasePath);
        _ = await secondRun.GetInitializedConnectionAsync();

        using var verification = new SQLiteConnection(tempDatabase.DatabasePath);
        var session = verification.Query<SessionRevisionRow>(
            "SELECT processed_telemetry_revision, track_projection_revision, updated FROM session WHERE id = ?",
            sessionId).Single();
        var track = verification.Query<TrackRevisionRow>(
            "SELECT points_revision, updated FROM track WHERE id = ?",
            trackId).Single();

        Assert.Equal(1, session.ProcessedTelemetryRevision);
        Assert.Equal(1, session.TrackProjectionRevision);
        Assert.Equal(10, session.Updated);
        Assert.Equal(1, track.PointsRevision);
        Assert.Equal(11, track.Updated);
    }

    [Fact]
    public async Task LocalContentRevisionTriggers_IncrementOnlyForEffectiveContentChanges_AndPreserveUpdated()
    {
        using var tempDatabase = new TempDatabase("local-content-revision-triggers.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        _ = await database.GetInitializedConnectionAsync();
        var sessionId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var points = AppJson.Serialize(new List<TrackPoint> { new(100, 1, 2, 3) });

        using var connection = new SQLiteConnection(tempDatabase.DatabasePath);
        connection.Execute(
            "INSERT INTO session (id, name, description, data, session_processing_fingerprint, track, timestamp, duration_seconds, gps_offset_seconds, full_track_id, processed_telemetry_revision, track_projection_revision, updated, deleted, has_data) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 99, 99, ?, NULL, 1)",
            sessionId, "session", string.Empty, new byte[] { 1 }, "a", points, 100, 1.0, 0.0, trackId, 10);
        connection.Execute(
            "INSERT INTO track (id, points, start_time, end_time, points_revision, updated, deleted) VALUES (?, ?, 100, 100, 99, ?, NULL)",
            trackId, points, 11);

        AssertRevisions(connection, sessionId, trackId, processed: 1, projection: 1, pointsRevision: 1, sessionUpdated: 10, trackUpdated: 11);

        connection.Execute("UPDATE session SET data = data, session_processing_fingerprint = session_processing_fingerprint, processed_telemetry_revision = 0, name = 'metadata' WHERE id = ?", sessionId);
        connection.Execute("UPDATE session SET track = track, timestamp = timestamp, duration_seconds = duration_seconds, gps_offset_seconds = gps_offset_seconds, full_track_id = full_track_id, track_projection_revision = 0 WHERE id = ?", sessionId);
        connection.Execute("UPDATE track SET points = points, points_revision = 0 WHERE id = ?", trackId);
        AssertRevisions(connection, sessionId, trackId, processed: 1, projection: 1, pointsRevision: 1, sessionUpdated: 10, trackUpdated: 11);

        connection.Execute("UPDATE session SET data = ?, processed_telemetry_revision = 0 WHERE id = ?", new byte[] { 2 }, sessionId);
        connection.Execute("UPDATE session SET gps_offset_seconds = 0.5, track_projection_revision = 0 WHERE id = ?", sessionId);
        connection.Execute("UPDATE track SET points = ?, points_revision = 0 WHERE id = ?", AppJson.Serialize(new List<TrackPoint> { new(101, 4, 5, 6) }), trackId);
        AssertRevisions(connection, sessionId, trackId, processed: 2, projection: 2, pointsRevision: 2, sessionUpdated: 10, trackUpdated: 11);
    }

    [Fact]
    public async Task Initialization_ReplacesExistingLocalRevisionTriggers()
    {
        using var tempDatabase = new TempDatabase("local-content-revision-trigger-replacement.db");
        var databasePath = tempDatabase.DatabasePath;
        var firstRun = new TestPersistenceHarness(databasePath);
        _ = await firstRun.GetInitializedConnectionAsync();
        var sessionId = Guid.NewGuid();

        using (var connection = new SQLiteConnection(databasePath))
        {
            connection.Execute(
                "INSERT INTO session (id, name, description, data, processed_telemetry_revision, track_projection_revision, updated, deleted, has_data) VALUES (?, ?, ?, ?, 0, 0, 10, NULL, 1)",
                sessionId, "session", string.Empty, new byte[] { 1 });
            connection.Execute("DROP TRIGGER session_processed_revision_after_update");
            connection.Execute(
                "CREATE TRIGGER session_processed_revision_after_update AFTER UPDATE OF data ON session BEGIN UPDATE session SET processed_telemetry_revision = 999 WHERE id = NEW.id; END");
        }

        var secondRun = new TestPersistenceHarness(databasePath);
        _ = await secondRun.GetInitializedConnectionAsync();

        using var verification = new SQLiteConnection(databasePath);
        verification.Execute("UPDATE session SET data = ? WHERE id = ?", new byte[] { 2 }, sessionId);
        var row = verification.Query<SessionRevisionRow>(
            "SELECT processed_telemetry_revision, track_projection_revision, updated FROM session WHERE id = ?",
            sessionId).Single();
        var triggerSql = verification.ExecuteScalar<string>(
            "SELECT sql FROM sqlite_master WHERE type = 'trigger' AND name = 'session_processed_revision_after_update'");

        Assert.Equal(2, row.ProcessedTelemetryRevision);
        Assert.Contains("WHEN", triggerSql, StringComparison.Ordinal);
        Assert.DoesNotContain("999", triggerSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalContentRevisionTriggers_DoNotRewriteNoOpWatchedAssignments()
    {
        using var tempDatabase = new TempDatabase("local-content-revision-noop-write.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        _ = await database.GetInitializedConnectionAsync();
        var sessionId = Guid.NewGuid();

        using var connection = new SQLiteConnection(tempDatabase.DatabasePath);
        connection.Execute(
            "INSERT INTO session (id, name, description, data, session_processing_fingerprint, processed_telemetry_revision, track_projection_revision, updated, deleted, has_data) VALUES (?, ?, ?, ?, ?, 0, 0, 10, NULL, 1)",
            sessionId, "session", string.Empty, new byte[] { 1 }, "a");
        var before = connection.ExecuteScalar<long>("SELECT total_changes()");

        connection.Execute(
            "UPDATE session SET data = data, session_processing_fingerprint = session_processing_fingerprint WHERE id = ?",
            sessionId);

        var after = connection.ExecuteScalar<long>("SELECT total_changes()");
        var row = connection.Query<SessionRevisionRow>(
            "SELECT processed_telemetry_revision, track_projection_revision, updated FROM session WHERE id = ?",
            sessionId).Single();
        Assert.Equal(1, after - before);
        Assert.Equal(1, row.ProcessedTelemetryRevision);
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
    public async Task Initialization_CreatesSyncDeltaIndexes()
    {
        using var tempDatabase = new TempDatabase("sync-indexes.db");
        var databasePath = tempDatabase.DatabasePath;

        var database = new TestPersistenceHarness(databasePath);
        _ = await database.GetAllAsync<Board>();

        using var connection = new SQLiteConnection(databasePath);
        var indexNames = connection.Query<SqliteMasterRow>(
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'index'
              AND name IN (
                'ix_board_updated', 'ix_board_deleted',
                'ix_bike_updated', 'ix_bike_deleted',
                'ix_setup_updated', 'ix_setup_deleted',
                'ix_session_updated', 'ix_session_deleted',
                'ix_track_updated', 'ix_track_deleted'
              )
            """)
            .Select(row => row.Name)
            .ToHashSet(StringComparer.Ordinal);

        string[] expected =
        [
            "ix_board_updated",
            "ix_board_deleted",
            "ix_bike_updated",
            "ix_bike_deleted",
            "ix_setup_updated",
            "ix_setup_deleted",
            "ix_session_updated",
            "ix_session_deleted",
            "ix_track_updated",
            "ix_track_deleted",
        ];

        Assert.Equal(
            expected.OrderBy(name => name, StringComparer.Ordinal),
            indexNames.OrderBy(name => name, StringComparer.Ordinal));
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

    [Theory]
    [InlineData(ExtensionMigratorValidationCase.DuplicateIds)]
    [InlineData(ExtensionMigratorValidationCase.DuplicateTableOwnership)]
    [InlineData(ExtensionMigratorValidationCase.ReservedCoreTableName)]
    public void Constructor_RejectsInvalidExtensionMigratorDefinitions(
        ExtensionMigratorValidationCase validationCase)
    {
        using var tempDatabase = new TempDatabase("invalid-extension-migrator.db");
        var databasePath = tempDatabase.DatabasePath;
        var (migrators, expectedMessages) = CreateInvalidExtensionMigratorCase(validationCase);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new TestPersistenceHarness(databasePath, migrators));

        foreach (var expectedMessage in expectedMessages)
        {
            Assert.Contains(expectedMessage, exception.Message);
        }
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
                new ExtensionDatabaseMigrationStep(1, context =>
                {
                    appliedSteps.Add(1);
                    context.Transaction.Insert(new TestExtensionRow
                    {
                        Id = "step-1",
                        Value = 1,
                    });
                }),
                new ExtensionDatabaseMigrationStep(2, context =>
                {
                    appliedSteps.Add(2);
                    context.Transaction.Insert(new TestExtensionRow
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
    public async Task Initialization_RollsBackExtensionStepAndVersion_WhenStepThrows()
    {
        using var tempDatabase = new TempDatabase("extension-migration-rollback.db");
        var databasePath = tempDatabase.DatabasePath;
        var migrator = new TestExtensionMigrator(
            "test",
            targetVersion: 1,
            [typeof(TestExtensionRow)],
            [
                new ExtensionDatabaseMigrationStep(1, context =>
                {
                    context.Transaction.Insert(new TestExtensionRow
                    {
                        Id = "rolled-back",
                        Value = 1,
                    });
                    throw new InvalidOperationException("fail extension migration");
                }),
            ]);
        var context = PersistenceTestData.CreateConnectionContext(databasePath, [migrator]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.GetInitializedConnectionAsync());

        using var connection = new SQLiteConnection(databasePath);
        Assert.Empty(connection.Table<TestExtensionRow>().ToList());
        Assert.Null(connection.Find<ExtensionSchemaVersion>("test"));
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

    [Theory]
    [InlineData(ProcessedSessionFingerprintCase.MissingLegacyFingerprint)]
    [InlineData(ProcessedSessionFingerprintCase.PreviousProcessingVersion)]
    [InlineData(ProcessedSessionFingerprintCase.LegacyRearSuspensionKind)]
    [InlineData(ProcessedSessionFingerprintCase.DependencyHashMismatch)]
    [InlineData(ProcessedSessionFingerprintCase.UnexpectedProcessingVersion)]
    public async Task Initialization_DoesNotBackfillProcessedSessionFingerprintCase(
        ProcessedSessionFingerprintCase fingerprintCase)
    {
        using var tempDatabase = new TempDatabase($"{fingerprintCase}.db");
        ProcessingFingerprint? staleFingerprint = null;
        var seedOptions = CreateProcessedSessionFingerprintSeedOptions(
            fingerprintCase,
            fingerprint => staleFingerprint = fingerprint);
        var seed = SeedProcessedSessionDatabase(
            tempDatabase.DatabasePath,
            seedOptions.CreateFingerprintJson,
            seedOptions.BikeSnapshot,
            seedOptions.StoreLegacyRearSuspensionKind);

        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var persisted = await database.GetSessionAsync(seed.Session.Id);
        var persistedBike = seedOptions.StoreLegacyRearSuspensionKind
            ? await database.GetAsync<Bike>(seed.Bike.Id)
            : seed.Bike;

        Assert.NotNull(persisted);
        Assert.NotNull(persistedBike);
        if (seedOptions.StoreLegacyRearSuspensionKind)
        {
            Assert.IsType<RearSuspensionSpec.Linkage>(persistedBike!.RearSuspension);
        }

        Assert.Equal(seed.Session.Updated, persisted.Updated);
        Assert.Equal(seed.ProcessedData, await database.GetSessionRawPsstAsync(seed.Session.Id));
        var fingerprintService = new ProcessingFingerprintService();
        var evaluation = fingerprintService.EvaluateState(
            SessionSnapshot.From(persisted),
            SetupSnapshot.From(seed.Setup, boardId: null),
            BikeSnapshot.From(persistedBike!),
            RecordedSessionSourceSnapshot.From(seed.Source));

        AssertProcessedSessionFingerprintCase(fingerprintCase, evaluation, staleFingerprint);
    }

    [Fact]
    public async Task Initialization_AddsGenerationToLegacySessionBlobSwapRequests()
    {
        using var tempDatabase = new TempDatabase("legacy-session-blob-swap-request.db");
        var sessionId = Guid.NewGuid();
        using (var connection = new SQLiteConnection(tempDatabase.DatabasePath))
        {
            connection.CreateTable<Session>();
            connection.Execute(
                $"CREATE TABLE {SessionBlobSwapRequestStore.TableName} (session_id TEXT PRIMARY KEY, target_fingerprint TEXT NOT NULL)");
            connection.Insert(new Session(sessionId, "session", string.Empty, null)
            {
                Updated = 10,
                ClientUpdated = 10,
            });
            connection.Execute(
                $"INSERT INTO {SessionBlobSwapRequestStore.TableName} (session_id, target_fingerprint) VALUES (?, ?)",
                sessionId,
                "target");
        }

        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var legacyRequest = await database.GetSessionBlobSwapRequestAsync(sessionId);

        Assert.NotNull(legacyRequest);
        Assert.Equal("target", legacyRequest!.TargetFingerprint);
        Assert.Null(legacyRequest.TargetGeneration);
        using var verificationConnection = new SQLiteConnection(tempDatabase.DatabasePath);
        Assert.Contains(
            verificationConnection.Query<TableColumnInfo>(
                $"PRAGMA table_info({SessionBlobSwapRequestStore.TableName})"),
            column => column.Name == "target_generation");

        var generation = new SessionProcessedGeneration(
            DurationSeconds: 80,
            DistanceMeters: 20,
            AscentMeters: 8,
            DescentMeters: 3,
            FullTrackId: Guid.NewGuid(),
            GpsOffsetSeconds: 1.25,
            Track: [new TrackPoint(100, 1, 2, 3)]);
        verificationConnection.Execute(
            $"UPDATE {SessionBlobSwapRequestStore.TableName} SET target_generation = ? WHERE session_id = ?",
            AppJson.Serialize(generation),
            sessionId);

        var migratedRequest = await database.GetSessionBlobSwapRequestAsync(sessionId);
        Assert.NotNull(migratedRequest?.TargetGeneration);
        Assert.Equal(80, migratedRequest!.TargetGeneration!.DurationSeconds);
        Assert.Equal(1.25, migratedRequest.TargetGeneration.GpsOffsetSeconds);
        Assert.Equal(100, Assert.Single(migratedRequest.TargetGeneration.Track!).Time);
    }

    [Fact]
    public async Task Initialization_RepairsAndMaintainsSessionBlobSwapRequestOwnership()
    {
        using var tempDatabase = new TempDatabase("session-blob-swap-request-integrity.db");
        var activeSessionId = Guid.NewGuid();
        var deletedSessionId = Guid.NewGuid();
        var hardDeletedSessionId = Guid.NewGuid();

        using (var connection = new SQLiteConnection(tempDatabase.DatabasePath))
        {
            connection.CreateTable<Session>();
            connection.Execute(SessionBlobSwapRequestStore.CreateTableSql);
            connection.Insert(new Session(activeSessionId, "active", string.Empty, null)
            {
                Updated = 10,
                ClientUpdated = 10,
            });
            connection.Insert(new Session(deletedSessionId, "deleted", string.Empty, null)
            {
                Updated = 10,
                ClientUpdated = 10,
                Deleted = 11,
            });
            connection.Insert(new Session(hardDeletedSessionId, "hard delete", string.Empty, null)
            {
                Updated = 10,
                ClientUpdated = 10,
            });
            foreach (var sessionId in new[] { activeSessionId, deletedSessionId, Guid.NewGuid() })
            {
                connection.Execute(
                    $"INSERT INTO {SessionBlobSwapRequestStore.TableName} (session_id, target_fingerprint) VALUES (?, ?)",
                    sessionId,
                    "target");
            }
        }

        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        _ = await database.GetSessionAsync(activeSessionId);

        using var verificationConnection = new SQLiteConnection(tempDatabase.DatabasePath);
        Assert.Equal(
            activeSessionId.ToString("D"),
            verificationConnection.ExecuteScalar<string>(
                $"SELECT session_id FROM {SessionBlobSwapRequestStore.TableName}"));

        verificationConnection.Execute(
            "UPDATE session SET deleted = ? WHERE id = ?",
            12,
            activeSessionId);
        Assert.Equal(
            0,
            verificationConnection.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM {SessionBlobSwapRequestStore.TableName}"));

        verificationConnection.Execute(
            $"INSERT INTO {SessionBlobSwapRequestStore.TableName} (session_id, target_fingerprint) VALUES (?, ?)",
            hardDeletedSessionId,
            "target");
        verificationConnection.Execute("DELETE FROM session WHERE id = ?", hardDeletedSessionId);
        Assert.Equal(
            0,
            verificationConnection.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM {SessionBlobSwapRequestStore.TableName}"));

        verificationConnection.Execute(
            $"INSERT INTO {SessionBlobSwapRequestStore.TableName} (session_id, target_fingerprint) VALUES (?, ?)",
            Guid.NewGuid(),
            "orphan");
        Assert.Empty(await database.GetRequestedSessionBlobSwapIdsAsync());
        Assert.Equal(
            0,
            verificationConnection.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM {SessionBlobSwapRequestStore.TableName}"));
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

    private static (IReadOnlyList<TestExtensionMigrator> Migrators, string[] ExpectedMessages)
        CreateInvalidExtensionMigratorCase(ExtensionMigratorValidationCase validationCase)
    {
        return validationCase switch
        {
            ExtensionMigratorValidationCase.DuplicateIds => (
                [
                    new TestExtensionMigrator("test", targetVersion: 0, [typeof(TestExtensionRow)], []),
                    new TestExtensionMigrator("test", targetVersion: 0, [typeof(SecondTestExtensionRow)], []),
                ],
                ["test"]),

            ExtensionMigratorValidationCase.DuplicateTableOwnership => (
                [
                    new TestExtensionMigrator("first", targetVersion: 0, [typeof(TestExtensionRow)], []),
                    new TestExtensionMigrator("second", targetVersion: 0, [typeof(DuplicateNamedExtensionRow)], []),
                ],
                ["test_extension_row", "first", "second"]),

            ExtensionMigratorValidationCase.ReservedCoreTableName => (
                [new TestExtensionMigrator("test", targetVersion: 0, [typeof(CoreNamedExtensionRow)], [])],
                ["session", "reserved"]),

            _ => throw new ArgumentOutOfRangeException(nameof(validationCase), validationCase, null),
        };
    }

    private static ProcessedSessionFingerprintSeedOptions CreateProcessedSessionFingerprintSeedOptions(
        ProcessedSessionFingerprintCase fingerprintCase,
        Action<ProcessingFingerprint> captureStaleFingerprint)
    {
        return fingerprintCase switch
        {
            ProcessedSessionFingerprintCase.MissingLegacyFingerprint => new ProcessedSessionFingerprintSeedOptions(
                CreateFingerprintJson: null,
                BikeSnapshot: null,
                StoreLegacyRearSuspensionKind: false),

            ProcessedSessionFingerprintCase.PreviousProcessingVersion => new ProcessedSessionFingerprintSeedOptions(
                seed =>
                {
                    var staleFingerprint = CreateCurrentFingerprint(seed) with
                    {
                        ProcessingVersion = TelemetryProcessingVersion.Current - 1
                    };
                    captureStaleFingerprint(staleFingerprint);
                    return AppJson.Serialize(staleFingerprint);
                },
                BikeSnapshot: null,
                StoreLegacyRearSuspensionKind: false),

            ProcessedSessionFingerprintCase.LegacyRearSuspensionKind => CreateLegacyRearSuspensionFingerprintSeedOptions(
                captureStaleFingerprint),

            ProcessedSessionFingerprintCase.DependencyHashMismatch => new ProcessedSessionFingerprintSeedOptions(
                seed =>
                {
                    var staleFingerprint = CreateCurrentFingerprint(seed) with
                    {
                        DependencyHash = "changed-dependency"
                    };
                    captureStaleFingerprint(staleFingerprint);
                    return AppJson.Serialize(staleFingerprint);
                },
                BikeSnapshot: null,
                StoreLegacyRearSuspensionKind: false),

            ProcessedSessionFingerprintCase.UnexpectedProcessingVersion => new ProcessedSessionFingerprintSeedOptions(
                seed =>
                {
                    var staleFingerprint = CreateCurrentFingerprint(seed) with
                    {
                        ProcessingVersion = 0
                    };
                    captureStaleFingerprint(staleFingerprint);
                    return AppJson.Serialize(staleFingerprint);
                },
                BikeSnapshot: null,
                StoreLegacyRearSuspensionKind: false),

            _ => throw new ArgumentOutOfRangeException(nameof(fingerprintCase), fingerprintCase, null),
        };
    }

    private static ProcessedSessionFingerprintSeedOptions CreateLegacyRearSuspensionFingerprintSeedOptions(
        Action<ProcessingFingerprint> captureStaleFingerprint)
    {
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var bikeSnapshot = TestSnapshots.Bike(id: Guid.NewGuid(), updated: 20) with
        {
            ShockStroke = linkage.ShockStroke,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage)
        };

        return new ProcessedSessionFingerprintSeedOptions(
            seed =>
            {
                var fingerprintService = new ProcessingFingerprintService();
                var legacyBike = BikeSnapshot.From(seed.Bike) with
                {
                    RearSuspension = new RearSuspensionSpec.Hardtail()
                };
                var staleFingerprint = fingerprintService.CreateCurrent(
                    SessionSnapshot.From(seed.Session),
                    SetupSnapshot.From(seed.Setup, boardId: null),
                    legacyBike,
                    RecordedSessionSourceSnapshot.From(seed.Source));
                captureStaleFingerprint(staleFingerprint);
                return AppJson.Serialize(staleFingerprint);
            },
            bikeSnapshot,
            StoreLegacyRearSuspensionKind: true);
    }

    private static void AssertProcessedSessionFingerprintCase(
        ProcessedSessionFingerprintCase fingerprintCase,
        ProcessingFingerprintEvaluation evaluation,
        ProcessingFingerprint? staleFingerprint)
    {
        if (fingerprintCase == ProcessedSessionFingerprintCase.MissingLegacyFingerprint)
        {
            Assert.Null(evaluation.Persisted);
            Assert.IsType<SessionStaleness.UnknownLegacyFingerprint>(evaluation.Staleness);
            return;
        }

        Assert.Equal(staleFingerprint, evaluation.Persisted);
        switch (fingerprintCase)
        {
            case ProcessedSessionFingerprintCase.PreviousProcessingVersion:
                var previous = Assert.IsType<SessionStaleness.ProcessingVersionChanged>(evaluation.Staleness);
                Assert.Equal(TelemetryProcessingVersion.Current - 1, previous.Persisted);
                Assert.Equal(TelemetryProcessingVersion.Current, previous.CurrentVersion);
                break;
            case ProcessedSessionFingerprintCase.LegacyRearSuspensionKind:
            case ProcessedSessionFingerprintCase.DependencyHashMismatch:
                Assert.IsType<SessionStaleness.DependencyHashChanged>(evaluation.Staleness);
                break;
            case ProcessedSessionFingerprintCase.UnexpectedProcessingVersion:
                Assert.IsType<SessionStaleness.ProcessingVersionChanged>(evaluation.Staleness);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fingerprintCase), fingerprintCase, null);
        }
    }

    private static void AssertRevisions(
        SQLiteConnection connection,
        Guid sessionId,
        Guid trackId,
        long processed,
        long projection,
        long pointsRevision,
        long sessionUpdated,
        long trackUpdated)
    {
        var session = connection.Query<SessionRevisionRow>(
            "SELECT processed_telemetry_revision, track_projection_revision, updated FROM session WHERE id = ?",
            sessionId).Single();
        var track = connection.Query<TrackRevisionRow>(
            "SELECT points_revision, updated FROM track WHERE id = ?",
            trackId).Single();
        Assert.Equal(processed, session.ProcessedTelemetryRevision);
        Assert.Equal(projection, session.TrackProjectionRevision);
        Assert.Equal(sessionUpdated, session.Updated);
        Assert.Equal(pointsRevision, track.PointsRevision);
        Assert.Equal(trackUpdated, track.Updated);
    }

    private sealed class SessionRevisionRow
    {
        [Column("processed_telemetry_revision")]
        public long ProcessedTelemetryRevision { get; set; }

        [Column("track_projection_revision")]
        public long TrackProjectionRevision { get; set; }

        [Column("updated")]
        public long Updated { get; set; }
    }

    private sealed class TrackRevisionRow
    {
        [Column("points_revision")]
        public long PointsRevision { get; set; }

        [Column("updated")]
        public long Updated { get; set; }
    }

    private sealed record SeededProcessedSession(
        Bike Bike,
        Setup Setup,
        Session Session,
        RecordedSessionSource Source,
        byte[] ProcessedData);

    private sealed record ProcessedSessionFingerprintSeedOptions(
        Func<SeededProcessedSession, string?>? CreateFingerprintJson,
        BikeSnapshot? BikeSnapshot,
        bool StoreLegacyRearSuspensionKind);

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
