using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.SyncAndPairing.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SQLite;
using Serilog;
using Sufni.Kinematics;

using Sufni.App.Bikes.Models;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Segments.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Setups.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Video.Models;
namespace Sufni.App.Infrastructure;

internal sealed class DatabaseMigrationRunner(
    string databasePath,
    SQLiteAsyncConnection connection,
    TimeProvider timeProvider,
    IReadOnlyDictionary<Guid, int> legacyProcessingWindows,
    IReadOnlyList<CustomTileLayerSnapshot> legacyCustomLayers)
{
    private static readonly ILogger logger = Log.ForContext<DatabaseMigrationRunner>();
    private static readonly string[] SyncIndexedTables = ["board", "bike", "setup", "session", "track", "session_segment", "custom_tile_layer"];
    private readonly CoreMigrationStore coreMigrations = new(connection);

    internal async Task RunAsync()
    {
        await connection.EnableWriteAheadLoggingAsync();
        await CreateTablesAsync();
        await EnsureSyncCursorColumnsAsync();
        await EnsureSyncIndexesAsync();
        await EnsureSessionProcessingFingerprintColumnAsync();
        await EnsureSessionDurationColumnAsync();
        await EnsureSessionGpsOffsetColumnAsync();
        await EnsureLocalContentRevisionColumnsAndTriggersAsync();
        await EnsureBikeDampingSpeedCutoffColumnsAsync();
        await EnsureBikeRearSuspensionColumnAsync();
        await DropSessionCacheTableAsync();
        await coreMigrations.EnsureTableAsync();
        await coreMigrations.ApplyAsync("database-incarnation", transaction =>
        {
            transaction.Execute("CREATE TABLE database_identity (id INTEGER PRIMARY KEY CHECK (id = 1), db_incarnation TEXT NOT NULL)");
            transaction.Execute("INSERT INTO database_identity (id, db_incarnation) VALUES (1, ?)", Guid.NewGuid().ToString());
        });
        await coreMigrations.MarkAppliedAsync("session-video");
        await MigrateSessionDerivationWindowsAsync();
        await MigrateSessionDomainAsync();
        await MigrateTrackMaterializationsAsync();
        await MigrateSyncAuthorityAsync();
        await EnsureSessionBlobSwapRequestIntegrityAsync();
        await coreMigrations.MarkAppliedAsync("session-segment");
        await DropSchemaVersionTableAsync();

        var cleanupSummary = await Cleanup();
        logger.Information("SQLite database initialized at {DatabasePath}", databasePath);
        logger.Verbose(
            "SQLite startup cleanup removed {SessionCount} sessions, {SegmentCount} segments, {TrackCount} tracks, {BoardCount} boards, {SetupCount} setups, {BikeCount} bikes, and {PairedDeviceCount} paired devices",
            cleanupSummary.Sessions,
            cleanupSummary.Segments,
            cleanupSummary.Tracks,
            cleanupSummary.Boards,
            cleanupSummary.Setups,
            cleanupSummary.Bikes,
            cleanupSummary.PairedDevices);
    }

    private Task MigrateTrackMaterializationsAsync() => coreMigrations.ApplyAsync("track-materializations", transaction =>
    {
        transaction.Execute("""
            CREATE TABLE session_gps_metrics (
                session_id TEXT PRIMARY KEY NOT NULL,
                gps_metrics_key TEXT NOT NULL,
                distance_meters REAL,
                ascent_meters REAL,
                descent_meters REAL)
            """);
        var columns = transaction.GetTableInfo("session").Select(column => column.Name).ToHashSet();
        string[] gpsColumns = ["distance_meters", "ascent_meters", "descent_meters"];
        var values = string.Join(", ", gpsColumns.Select(column => columns.Contains(column) ? column : "NULL"));
        transaction.Execute($"INSERT INTO session_gps_metrics SELECT id, '', {values} FROM session");
        if (transaction.GetTableInfo("session_segment").Any(column => column.Name == "session_id"))
        {
            transaction.Execute("ALTER TABLE session_segment ADD COLUMN owner_key TEXT NOT NULL DEFAULT ''");
            transaction.Execute("""
                UPDATE session_segment SET
                    owner_key = (SELECT source_hash FROM session_recording_source source JOIN session
                        ON source.session_id = COALESCE(session.source_session_id, session.id)
                        WHERE session.id = session_segment.session_id),
                    start_seconds = start_seconds + COALESCE((SELECT window_start_seconds FROM session WHERE id = session_segment.session_id), 0),
                    end_seconds = end_seconds + COALESCE((SELECT window_start_seconds FROM session WHERE id = session_segment.session_id), 0)
                WHERE session_id IN (SELECT session.id FROM session JOIN session_recording_source source
                    ON source.session_id = COALESCE(session.source_session_id, session.id))
                """);
            transaction.Execute("DROP INDEX IF EXISTS session_segment_session_id");
            transaction.Execute("ALTER TABLE session_segment DROP COLUMN session_id");
            transaction.Execute("CREATE INDEX session_segment_owner_key ON session_segment(owner_key)");
        }
        if (transaction.GetTableInfo("session_video").Any(column => column.Name == "session_id"))
        {
            transaction.Execute("ALTER TABLE session_video ADD COLUMN owner_key TEXT NOT NULL DEFAULT ''");
            var owners = transaction.Query<LegacyVideoOwnerRow>("""
                SELECT clip.id, clip.session_id, clip.path, source.source_hash,
                    COALESCE(session.source_session_id, session.id) AS source_id, session.timestamp,
                    session.window_start_seconds, session.window_end_seconds, session.duration_seconds
                FROM session_video clip JOIN session ON session.id = clip.session_id
                JOIN session_recording_source source ON source.session_id = COALESCE(session.source_session_id, session.id)
                """);
            foreach (var group in owners.GroupBy(row => (row.SourceHash, row.Path)))
            {
                // The source-owning parent represents the pre-split recording window.
                var kept = group.OrderByDescending(row => row.SessionId == row.SourceId)
                    .ThenByDescending(row => row.WindowEndSeconds is { } end
                        ? end - row.WindowStartSeconds : row.DurationSeconds ?? double.PositiveInfinity)
                    .ThenBy(row => row.Id).First();
                var clip = transaction.Get<SessionVideoClip>(kept.Id);
                clip.OwnerKey = kept.SourceHash;
                clip.AlignmentOffsetSeconds += (kept.Timestamp ?? clip.StartTimestamp) - clip.StartTimestamp - kept.WindowStartSeconds;
                transaction.Update(clip);
                foreach (var dropped in group.Where(row => row.Id != kept.Id))
                {
                    transaction.Delete<SessionVideoClip>(dropped.Id);
                    logger.Information("Dropped video alignment {ClipId} from session {SessionId}; retained {KeptClipId} from session {KeptSessionId}",
                        dropped.Id, dropped.SessionId, kept.Id, kept.SessionId);
                }
            }
            transaction.Execute("DROP INDEX IF EXISTS session_video_session_id");
            transaction.Execute("ALTER TABLE session_video DROP COLUMN session_id");
            transaction.Execute("CREATE INDEX session_video_owner_key ON session_video(owner_key)");
        }
        foreach (var session in transaction.Query<Session>("SELECT id, gps_offset_seconds, window_start_seconds FROM session"))
            transaction.Execute("UPDATE session SET gps_offset_seconds = ? WHERE id = ?",
                session.GpsOffsetSeconds - SessionTrackTimeline.FractionalOrigin(session.WindowStartSeconds), session.Id);
        var groups = transaction.Table<Track>().ToList()
            .Where(track => track.HasPoints)
            .GroupBy(track => TrackContentHash.Compute(track.Points))
            .ToList();
        foreach (var group in groups)
        {
            var canonical = group.OrderBy(track => track.Deleted is not null).ThenBy(track => track.Id).First();
            foreach (var track in group)
            {
                transaction.Execute("DELETE FROM track WHERE id = ?", track.Id);
            }
            canonical.Id = group.Key;
            transaction.Insert(canonical);
        }
        if (transaction.GetTableInfo(SessionBlobSwapRequestStore.TableName).Any(column => column.Name == "target_generation"))
            foreach (var row in transaction.Query<LegacySessionGenerationRow>(
                         "SELECT session_id, target_generation FROM session_blob_swap_request WHERE target_generation IS NOT NULL"))
                transaction.Execute("UPDATE session_blob_swap_request SET target_generation = ? WHERE session_id = ?",
                    AppJson.Serialize(AppJson.Deserialize<SessionProcessedGeneration>(row.Generation)), row.SessionId);
        foreach (var column in new[] { "track", "track_projection_revision", "full_track_id", "distance_meters", "ascent_meters", "descent_meters" })
            if (transaction.GetTableInfo("session").Any(info => info.Name == column))
                transaction.Execute($"ALTER TABLE session DROP COLUMN {column}");
    });

    private Task MigrateSessionDomainAsync() => coreMigrations.ApplyAsync("session-domain-ownership", transaction =>
    {
        if (transaction.GetTableInfo("session").Any(column => column.Name == "has_data"))
            transaction.Execute("ALTER TABLE session DROP COLUMN has_data");
        foreach (var source in transaction.Table<RecordedSessionSource>())
            transaction.Execute("UPDATE session_recording_source SET source_hash = ? WHERE session_id = ?",
                RecordedSessionSourceHash.Compute(source.SourceKind, source.Payload), source.SessionId);
        foreach (var (sessionId, window) in legacyProcessingWindows)
            transaction.Execute("UPDATE session SET velocity_filter_window_ms = ? WHERE id = ? AND velocity_filter_window_ms IS NULL", window, sessionId);
    });

    private async Task CreateTablesAsync()
    {
        await connection.CreateTablesAsync(CreateFlags.None,
        [
            typeof(Board),
            typeof(Setup),
            typeof(Bike),
            typeof(Session),
            typeof(RecordedSessionSource),
            typeof(Synchronization),
            typeof(PairedDevice),
            typeof(Track),
            typeof(CustomTileLayer)
        ]);
        // Existing attachment columns migrate with their anchors in the versioned transaction.
        if ((await connection.GetTableInfoAsync("session_video")).Count == 0)
            await connection.CreateTableAsync<SessionVideoClip>();
        if ((await connection.GetTableInfoAsync("session_segment")).Count == 0)
            await connection.CreateTableAsync<SessionSegment>();
    }

    private Task EnsureSyncCursorColumnsAsync() =>
        EnsureColumnsAsync(
            "sync",
            ("last_push_time", "INTEGER NOT NULL DEFAULT 0"),
            ("last_pull_time", "INTEGER NOT NULL DEFAULT 0"));

    private Task MigrateSyncAuthorityAsync() => coreMigrations.ApplyAsync("sync-authority", transaction =>
    {
        transaction.Execute("CREATE TABLE sync_sequence (id INTEGER PRIMARY KEY CHECK (id = 1), local_seq INTEGER NOT NULL, sync_seq INTEGER NOT NULL)");
        transaction.Execute("INSERT INTO sync_sequence VALUES (1, 0, 0)");
        transaction.Execute("""
            CREATE TABLE sync_deletion (
                entity_table TEXT NOT NULL, id TEXT NOT NULL, version INTEGER NOT NULL,
                updated INTEGER NOT NULL, updated_by TEXT NOT NULL, deleted INTEGER NOT NULL,
                local_seq INTEGER NOT NULL, sync_seq INTEGER NOT NULL,
                PRIMARY KEY (entity_table, id))
            """);
        foreach (var snapshot in legacyCustomLayers)
        {
            transaction.Execute("INSERT OR IGNORE INTO custom_tile_layer (id, name, url_template, attribution_text, attribution_url, max_zoom, updated) VALUES (?, ?, ?, ?, ?, ?, ?)",
                snapshot.Id, snapshot.Name, snapshot.UrlTemplate, snapshot.AttributionText, snapshot.AttributionUrl, snapshot.MaxZoom, snapshot.Updated);
        }
        foreach (var table in SyncIndexedTables)
        {
            var names = transaction.GetTableInfo(table).Select(column => column.Name).ToHashSet();
            foreach (var (name, type) in new[] { ("version", "INTEGER"), ("updated_by", "TEXT"), ("local_seq", "INTEGER"), ("sync_seq", "INTEGER") })
                if (!names.Contains(name)) transaction.Execute($"ALTER TABLE {table} ADD COLUMN {name} {type}");
            foreach (var row in transaction.Query<Synchronizable>($"SELECT id FROM {table} ORDER BY id"))
                transaction.Execute($"UPDATE {table} SET version=1, updated_by='', local_seq=?, sync_seq=? WHERE id=?",
                    SynchronizationWriteMetadata.NextLocalSequence(transaction), SynchronizationWriteMetadata.NextSyncSequence(transaction), row.Id);
            transaction.Execute($"INSERT INTO sync_deletion SELECT ?, id, version, updated, updated_by, deleted, local_seq, sync_seq FROM {table} WHERE deleted IS NOT NULL", table);
            if (names.Contains("client_updated")) transaction.Execute($"ALTER TABLE {table} DROP COLUMN client_updated");
            transaction.Execute($"CREATE INDEX ix_{table}_local_seq ON {table}(local_seq)");
            transaction.Execute($"CREATE INDEX ix_{table}_sync_seq ON {table}(sync_seq)");
        }
        transaction.Execute("CREATE INDEX ix_sync_deletion_local_seq ON sync_deletion(local_seq)");
        transaction.Execute("CREATE INDEX ix_sync_deletion_sync_seq ON sync_deletion(sync_seq)");
        var columns = transaction.GetTableInfo(SessionBlobSwapRequestStore.TableName);
        if (columns.Count > 0 && columns.All(column => column.Name != "target_generation"))
            transaction.Execute($"DROP TABLE {SessionBlobSwapRequestStore.TableName}");
        transaction.Execute(SessionBlobSwapRequestStore.CreateTableSql);
        transaction.Execute($"DELETE FROM {SessionBlobSwapRequestStore.TableName} WHERE target_generation IS NULL");
    });

    private async Task EnsureSessionBlobSwapRequestIntegrityAsync()
    {
        await connection.ExecuteAsync(
            $"""
            DELETE FROM {SessionBlobSwapRequestStore.TableName}
            WHERE NOT EXISTS (
                SELECT 1
                FROM session
                WHERE session.id = {SessionBlobSwapRequestStore.TableName}.session_id
                  AND session.deleted IS NULL
            )
            """);

        await connection.ExecuteAsync("DROP TRIGGER IF EXISTS session_blob_swap_request_after_session_delete");
        await connection.ExecuteAsync("DROP TRIGGER IF EXISTS session_blob_swap_request_after_session_soft_delete");
        await connection.ExecuteAsync(
            $"""
            CREATE TRIGGER session_blob_swap_request_after_session_delete
            AFTER DELETE ON session
            BEGIN
                DELETE FROM {SessionBlobSwapRequestStore.TableName}
                WHERE session_id = OLD.id;
            END
            """);
        await connection.ExecuteAsync(
            $"""
            CREATE TRIGGER session_blob_swap_request_after_session_soft_delete
            AFTER UPDATE OF deleted ON session
            WHEN NEW.deleted IS NOT NULL
            BEGIN
                DELETE FROM {SessionBlobSwapRequestStore.TableName}
                WHERE session_id = NEW.id;
            END
            """);
    }

    // One-time move of trim/split windows onto the session row. Windows come from
    // the table of older builds; sessions without a row there take
    // the window from their processing fingerprint, because installs that received
    // derived sessions through sync hold only that copy.
    private async Task MigrateSessionDerivationWindowsAsync()
    {
        const string migrationId = "session-derivation-window";
        if (await coreMigrations.IsAppliedAsync(migrationId))
        {
            return;
        }

        var hasRangeTable = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'private_session_source_range'") > 0;
        await connection.RunInTransactionAsync(transaction =>
        {
            if (hasRangeTable)
            {
                transaction.Execute(
                    """
                    UPDATE session
                    SET (source_session_id, window_start_seconds, window_end_seconds,
                         previous_window_start_seconds, previous_window_end_seconds) = (
                        SELECT range.source_session_id, range.range_start_seconds, range.range_end_seconds,
                               range.prev_range_start_seconds, range.prev_range_end_seconds
                        FROM private_session_source_range range
                        WHERE range.session_id = session.id)
                    WHERE EXISTS (
                        SELECT 1
                        FROM private_session_source_range range
                        WHERE range.session_id = session.id
                          AND range.deleted IS NULL
                          AND NOT (range.source_session_id = range.session_id
                                   AND range.range_start_seconds = 0
                                   AND range.range_end_seconds IS NULL))
                    """);
            }

            var fingerprintRows = transaction.Query<SessionFingerprintRow>(
                hasRangeTable
                    ? """
                      SELECT id, session_processing_fingerprint
                      FROM session
                      WHERE source_session_id IS NULL
                        AND session_processing_fingerprint IS NOT NULL
                        AND id NOT IN (SELECT session_id FROM private_session_source_range)
                      """
                    : """
                      SELECT id, session_processing_fingerprint
                      FROM session
                      WHERE source_session_id IS NULL
                        AND session_processing_fingerprint IS NOT NULL
                      """);
            foreach (var row in fingerprintRows)
            {
                if (TryReadDerivationWindow(row.ProcessingFingerprintJson) is not { } window)
                {
                    continue;
                }

                transaction.Execute(
                    "UPDATE session SET source_session_id = ?, window_start_seconds = ?, window_end_seconds = ? WHERE id = ?",
                    window.SourceSessionId,
                    window.StartSeconds,
                    window.EndSeconds,
                    row.Id);
            }

            if (hasRangeTable)
            {
                transaction.Execute("DROP TABLE private_session_source_range");
            }
        });
        await coreMigrations.MarkAppliedAsync(migrationId);
    }

    // The schema versions of the older tables that the copy migrations above
    // replaced.
    private Task DropSchemaVersionTableAsync() =>
        connection.ExecuteAsync("DROP TABLE IF EXISTS extension_schema_version");

    private static RecordedSessionDerivationWindow? TryReadDerivationWindow(string? processingFingerprintJson)
    {
        try
        {
            var window = AppJson.Deserialize<ProcessingFingerprint>(processingFingerprintJson!)?.DerivationWindow;
            return window is not null && window.SourceSessionId != Guid.Empty ? window : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private async Task EnsureSyncIndexesAsync()
    {
        foreach (var table in SyncIndexedTables)
        {
            await connection.ExecuteAsync($"CREATE INDEX IF NOT EXISTS ix_{table}_updated ON {table}(updated)");
            await connection.ExecuteAsync($"CREATE INDEX IF NOT EXISTS ix_{table}_deleted ON {table}(deleted)");
        }
    }

    private Task EnsureSessionProcessingFingerprintColumnAsync() =>
        EnsureColumnsAsync("session", ("session_processing_fingerprint", "TEXT"));

    private Task EnsureSessionDurationColumnAsync() =>
        EnsureColumnsAsync("session", ("duration_seconds", "REAL"));

    private async Task EnsureSessionGpsOffsetColumnAsync()
    {
        await EnsureColumnsAsync("session", ("gps_offset_seconds", "REAL"));
        await connection.ExecuteAsync("UPDATE session SET gps_offset_seconds = 0 WHERE gps_offset_seconds IS NULL");
    }

    private async Task EnsureLocalContentRevisionColumnsAndTriggersAsync()
    {
        await EnsureColumnsAsync(
            "session",
            ("processed_telemetry_revision", "INTEGER NOT NULL DEFAULT 0"));
        await EnsureColumnsAsync("track", ("points_revision", "INTEGER NOT NULL DEFAULT 0"));

        await connection.ExecuteAsync(
            """
            UPDATE session
            SET processed_telemetry_revision = CASE WHEN data IS NOT NULL THEN 1 ELSE 0 END
            WHERE processed_telemetry_revision IS NULL
               OR (processed_telemetry_revision = 0 AND data IS NOT NULL)
            """);
        await connection.ExecuteAsync(
            """
            UPDATE track
            SET points_revision = CASE WHEN points IS NOT NULL THEN 1 ELSE 0 END
            WHERE points_revision IS NULL
               OR (points_revision = 0 AND points IS NOT NULL)
            """);

        foreach (var triggerName in new[]
                 {
                     "session_local_revisions_after_insert",
                     "session_processed_revision_after_update",
                     "session_track_projection_revision_after_update",
                     "track_points_revision_after_insert",
                     "track_points_revision_after_update",
                 })
        {
            await connection.ExecuteAsync($"DROP TRIGGER IF EXISTS {triggerName}");
        }

        await connection.ExecuteAsync(
            """
            CREATE TRIGGER session_local_revisions_after_insert
            AFTER INSERT ON session
            WHEN NEW.processed_telemetry_revision IS NOT (CASE WHEN NEW.data IS NOT NULL THEN 1 ELSE 0 END)
            BEGIN
                UPDATE session
                SET processed_telemetry_revision = CASE WHEN NEW.data IS NOT NULL THEN 1 ELSE 0 END
                WHERE id = NEW.id;
            END
            """);
        await connection.ExecuteAsync(
            """
            CREATE TRIGGER session_processed_revision_after_update
            AFTER UPDATE OF data, session_processing_fingerprint ON session
            WHEN NEW.data IS NOT OLD.data
              OR NEW.session_processing_fingerprint IS NOT OLD.session_processing_fingerprint
              OR NEW.processed_telemetry_revision IS NOT OLD.processed_telemetry_revision
            BEGIN
                UPDATE session
                SET processed_telemetry_revision = CASE
                    WHEN NEW.data IS NOT OLD.data
                      OR NEW.session_processing_fingerprint IS NOT OLD.session_processing_fingerprint
                    THEN COALESCE(
                        OLD.processed_telemetry_revision,
                        CASE WHEN OLD.data IS NOT NULL THEN 1 ELSE 0 END) + 1
                    ELSE COALESCE(
                        OLD.processed_telemetry_revision,
                        CASE WHEN OLD.data IS NOT NULL THEN 1 ELSE 0 END)
                END
                WHERE id = NEW.id;
            END
            """);
        await connection.ExecuteAsync(
            """
            CREATE TRIGGER track_points_revision_after_insert
            AFTER INSERT ON track
            WHEN NEW.points_revision IS NOT (CASE WHEN NEW.points IS NOT NULL THEN 1 ELSE 0 END)
            BEGIN
                UPDATE track
                SET points_revision = CASE WHEN NEW.points IS NOT NULL THEN 1 ELSE 0 END
                WHERE id = NEW.id;
            END
            """);
        await connection.ExecuteAsync(
            """
            CREATE TRIGGER track_points_revision_after_update
            AFTER UPDATE OF points ON track
            WHEN NEW.points IS NOT OLD.points
              OR NEW.points_revision IS NOT OLD.points_revision
            BEGIN
                UPDATE track
                SET points_revision = CASE
                    WHEN NEW.points IS NOT OLD.points THEN COALESCE(
                        OLD.points_revision,
                        CASE WHEN OLD.points IS NOT NULL THEN 1 ELSE 0 END) + 1
                    ELSE COALESCE(
                        OLD.points_revision,
                        CASE WHEN OLD.points IS NOT NULL THEN 1 ELSE 0 END)
                END
                WHERE id = NEW.id;
            END
            """);
    }

    private async Task EnsureColumnsAsync(
        string table,
        params (string Name, string Declaration)[] requiredColumns)
    {
        var columns = await connection.QueryAsync<TableColumnInfo>($"PRAGMA table_info({table})");
        var columnNames = columns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, declaration) in requiredColumns)
        {
            if (columnNames.Add(name))
            {
                await connection.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {name} {declaration}");
            }
        }
    }

    private async Task EnsureBikeDampingSpeedCutoffColumnsAsync()
    {
        await EnsureColumnsAsync(
            "bike",
            BikeDampingSpeedCutoffColumnNames
                .Select(static columnName => (columnName, "REAL"))
                .ToArray());

        foreach (var columnName in BikeDampingSpeedCutoffColumnNames)
        {
            await connection.ExecuteAsync(
                $"UPDATE bike SET {columnName} = ? WHERE {columnName} IS NULL",
                DampingSpeedCutoffs.DefaultMmPerSecond);
        }
    }

    private Task DropSessionCacheTableAsync() =>
        connection.ExecuteAsync("DROP TABLE IF EXISTS session_cache");

    private async Task EnsureBikeRearSuspensionColumnAsync()
    {
        var columns = await connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(bike)");
        var columnNames = columns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!columnNames.Contains("rear_suspension"))
        {
            await connection.ExecuteAsync(
                "ALTER TABLE bike ADD COLUMN rear_suspension TEXT NOT NULL DEFAULT '{\"kind\":\"hardtail\"}'");
            columnNames.Add("rear_suspension");
        }

        if (!columnNames.Contains("rear_suspension_kind") &&
            !columnNames.Contains("linkage") &&
            !columnNames.Contains("leverage_ratio"))
        {
            return;
        }

        var rearSuspensionKindColumn = columnNames.Contains("rear_suspension_kind")
            ? "rear_suspension_kind"
            : "NULL AS rear_suspension_kind";
        var linkageColumn = columnNames.Contains("linkage")
            ? "linkage"
            : "NULL AS linkage";
        var leverageRatioColumn = columnNames.Contains("leverage_ratio")
            ? "leverage_ratio"
            : "NULL AS leverage_ratio";
        var rows = await connection.QueryAsync<LegacyBikeRearSuspensionRow>(
            $"""
            SELECT id, {rearSuspensionKindColumn}, {linkageColumn}, {leverageRatioColumn}, shock_stroke
            FROM bike
            """);

        foreach (var row in rows)
        {
            var rearSuspension = MapLegacyRearSuspension(row, out var reconciledShockStroke);
            var json = RearSuspensionJsonCodec.Serialize(rearSuspension);
            if (reconciledShockStroke.HasValue)
            {
                await connection.ExecuteAsync(
                    "UPDATE bike SET rear_suspension = ?, shock_stroke = ? WHERE id = ?",
                    json,
                    reconciledShockStroke.Value,
                    row.Id);
            }
            else
            {
                await connection.ExecuteAsync(
                    "UPDATE bike SET rear_suspension = ? WHERE id = ?",
                    json,
                    row.Id);
                }
        }

        foreach (var legacyColumn in new[] { "rear_suspension_kind", "linkage", "leverage_ratio" })
        {
            if (columnNames.Contains(legacyColumn))
            {
                await connection.ExecuteAsync($"ALTER TABLE bike DROP COLUMN {legacyColumn}");
            }
        }
    }

    private static RearSuspensionSpec MapLegacyRearSuspension(
        LegacyBikeRearSuspensionRow row,
        out double? reconciledShockStroke)
    {
        reconciledShockStroke = null;
        var kind = TryReadRearSuspensionKind(row.RearSuspensionKind);
        var hasLinkage = TryParseLinkage(row.LinkageJson, out var linkage);
        var hasLeverageRatio = TryParseLeverageRatio(row.LeverageRatioJson, out var leverageRatio);

        return kind switch
        {
            RearSuspensionKind.Linkage => hasLinkage
                ? Linkage(linkage!, row.ShockStroke, out reconciledShockStroke)
                : new RearSuspensionSpec.LinkageDraft(),

            RearSuspensionKind.LeverageRatio => hasLeverageRatio
                ? new RearSuspensionSpec.LeverageRatio(leverageRatio!)
                : new RearSuspensionSpec.LeverageRatioDraft(),

            _ when hasLinkage =>
                Linkage(linkage!, row.ShockStroke, out reconciledShockStroke),

            _ when hasLeverageRatio =>
                new RearSuspensionSpec.LeverageRatio(leverageRatio!),

            _ => new RearSuspensionSpec.Hardtail(),
        };
    }

    private static RearSuspensionKind? TryReadRearSuspensionKind(int? value)
    {
        return value is null || !Enum.IsDefined(typeof(RearSuspensionKind), value.Value)
            ? null
            : (RearSuspensionKind)value.Value;
    }

    private static RearSuspensionSpec.Linkage Linkage(
        LinkageSpec linkage,
        double? shockStroke,
        out double? reconciledShockStroke)
    {
        if (shockStroke.HasValue)
        {
            reconciledShockStroke = shockStroke.Value;
            return new RearSuspensionSpec.Linkage(linkage.WithShockStroke(shockStroke.Value));
        }

        reconciledShockStroke = linkage.ShockStroke;
        return new RearSuspensionSpec.Linkage(linkage);
    }

    private static bool TryParseLinkage(string? json, out LinkageSpec? linkage)
    {
        linkage = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            linkage = LinkageSpec.FromJson(json);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParseLeverageRatio(string? json, out LeverageRatioSpec? leverageRatio)
    {
        leverageRatio = LeverageRatioSpec.FromJson(json ?? string.Empty);
        return leverageRatio is not null;
    }

    private async Task<CleanupSummary> Cleanup()
    {
        var oneDayAgo = timeProvider.GetLocalNow().AddDays(-1).ToUnixTimeSeconds();

        var deletedSessions = await connection.Table<Session>().DeleteAsync(session => session.Deleted != null && session.Deleted < oneDayAgo);
        await connection.ExecuteAsync("DELETE FROM session_gps_metrics WHERE session_id NOT IN (SELECT id FROM session)");
        var deletedSegments = await connection.Table<SessionSegment>().DeleteAsync(segment => segment.Deleted != null && segment.Deleted < oneDayAgo);
        var deletedTracks = await connection.Table<Track>().DeleteAsync(track => track.Deleted != null && track.Deleted < oneDayAgo);
        var deletedBoards = await connection.Table<Board>().DeleteAsync(board => board.Deleted != null && board.Deleted < oneDayAgo);
        var deletedSetups = await connection.Table<Setup>().DeleteAsync(setup => setup.Deleted != null && setup.Deleted < oneDayAgo);
        var deletedBikes = await connection.Table<Bike>().DeleteAsync(bike => bike.Deleted != null && bike.Deleted < oneDayAgo);
        await connection.Table<CustomTileLayer>().DeleteAsync(layer => layer.Deleted != null && layer.Deleted < oneDayAgo);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var deletedPairedDevices = await connection.Table<PairedDevice>().DeleteAsync(device => device.Expires < now);

        return new CleanupSummary(
            deletedSessions,
            deletedSegments,
            deletedTracks,
            deletedBoards,
            deletedSetups,
            deletedBikes,
            deletedPairedDevices);
    }

    private static readonly string[] DampingSpeedCutoffColumnNames =
    [
        "front_compression_damping_cutoff_mm_per_second",
        "front_rebound_damping_cutoff_mm_per_second",
        "rear_compression_damping_cutoff_mm_per_second",
        "rear_rebound_damping_cutoff_mm_per_second"
    ];

    private static readonly string[] BikeDampingSpeedCutoffColumnNames = DampingSpeedCutoffColumnNames;

    private sealed class LegacySessionGenerationRow
    {
        [Column("session_id")]
        public Guid SessionId { get; set; }

        [Column("target_generation")]
        public string Generation { get; set; } = null!;
    }

    private sealed class SessionFingerprintRow
    {
        [Column("id")]
        public Guid Id { get; set; }

        [Column("session_processing_fingerprint")]
        public string? ProcessingFingerprintJson { get; set; }
    }

    private sealed class TableColumnInfo
    {
        [Column("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class LegacyBikeRearSuspensionRow
    {
        [Column("id")]
        public Guid Id { get; set; }

        [Column("rear_suspension_kind")]
        public int? RearSuspensionKind { get; set; }

        [Column("linkage")]
        public string? LinkageJson { get; set; }

        [Column("leverage_ratio")]
        public string? LeverageRatioJson { get; set; }

        [Column("shock_stroke")]
        public double? ShockStroke { get; set; }
    }

    private sealed class LegacyVideoOwnerRow
    {
        [Column("id")] public Guid Id { get; set; }
        [Column("session_id")] public Guid SessionId { get; set; }
        [Column("source_id")] public Guid SourceId { get; set; }
        [Column("source_hash")] public string SourceHash { get; set; } = string.Empty;
        [Column("path")] public string Path { get; set; } = string.Empty;
        [Column("timestamp")] public long? Timestamp { get; set; }
        [Column("window_start_seconds")] public double WindowStartSeconds { get; set; }
        [Column("window_end_seconds")] public double? WindowEndSeconds { get; set; }
        [Column("duration_seconds")] public double? DurationSeconds { get; set; }
    }

    private sealed record CleanupSummary(
        int Sessions,
        int Segments,
        int Tracks,
        int Boards,
        int Setups,
        int Bikes,
        int PairedDevices);
}
