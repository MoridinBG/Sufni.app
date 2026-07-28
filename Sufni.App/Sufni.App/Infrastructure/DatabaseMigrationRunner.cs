using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SQLite;
using Serilog;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Kinematics;

using Sufni.App.Extensibility.Database;
using Sufni.App.Bikes.Models;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Setups.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Sessions.Services;
namespace Sufni.App.Infrastructure;

internal sealed class DatabaseMigrationRunner(
    string databasePath,
    SQLiteAsyncConnection connection,
    ExtensionDatabaseMigratorRunner extensionMigratorRunner,
    ExtensionCascadeService extensionCascadeService)
{
    private static readonly ILogger logger = Log.ForContext<DatabaseMigrationRunner>();
    private static readonly string[] SyncIndexedTables = ["board", "bike", "setup", "session", "track"];
    private readonly CoreMigrationStore coreMigrations = new(connection);

    internal async Task RunAsync()
    {
        try
        {
            await connection.EnableWriteAheadLoggingAsync();
            await CreateTablesAsync();
            await EnsureSyncCursorColumnsAsync();
            await EnsureSyncIndexesAsync();
            await EnsureSessionProcessingFingerprintColumnAsync();
            await EnsureSessionSummaryMetricColumnsAsync();
            await EnsureSessionGpsOffsetColumnAsync();
            await EnsureLocalContentRevisionColumnsAndTriggersAsync();
            await EnsureBikeDampingSpeedCutoffColumnsAsync();
            await EnsureBikeRearSuspensionColumnAsync();
            await DropSessionCacheTableAsync();
            await coreMigrations.EnsureTableAsync();
            await connection.ExecuteAsync(SessionBlobSwapRequestStore.CreateTableSql);
            await EnsureSessionBlobSwapRequestColumnsAsync();
            await EnsureSessionBlobSwapRequestIntegrityAsync();
            await extensionMigratorRunner.RunAsync(connection);

            var cleanupSummary = await Cleanup();
            await extensionCascadeService.RepairOrphansAsync(refreshExtensionState: false);
            logger.Information("SQLite database initialized at {DatabasePath}", databasePath);
            logger.Verbose(
                "SQLite startup cleanup removed {SessionCount} sessions, {TrackCount} tracks, {BoardCount} boards, {SetupCount} setups, {BikeCount} bikes, and {PairedDeviceCount} paired devices",
                cleanupSummary.Sessions,
                cleanupSummary.Tracks,
                cleanupSummary.Boards,
                cleanupSummary.Setups,
                cleanupSummary.Bikes,
                cleanupSummary.PairedDevices);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "SQLite database initialization failed at {DatabasePath}", databasePath);
            throw;
        }
    }

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
            typeof(Track)
        ]);
    }

    private Task EnsureSyncCursorColumnsAsync() =>
        EnsureColumnsAsync(
            "sync",
            ("last_push_time", "INTEGER NOT NULL DEFAULT 0"),
            ("last_pull_time", "INTEGER NOT NULL DEFAULT 0"));

    private Task EnsureSessionBlobSwapRequestColumnsAsync() =>
        EnsureColumnsAsync(
            SessionBlobSwapRequestStore.TableName,
            ("target_generation", "TEXT"));

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

    private Task EnsureSessionSummaryMetricColumnsAsync() =>
        EnsureColumnsAsync(
            "session",
            SessionSummaryMetricColumnNames
                .Select(static columnName => (columnName, "REAL"))
                .ToArray());

    private async Task EnsureSessionGpsOffsetColumnAsync()
    {
        await EnsureColumnsAsync("session", ("gps_offset_seconds", "REAL"));
        await connection.ExecuteAsync("UPDATE session SET gps_offset_seconds = 0 WHERE gps_offset_seconds IS NULL");
    }

    private async Task EnsureLocalContentRevisionColumnsAndTriggersAsync()
    {
        await EnsureColumnsAsync(
            "session",
            ("processed_telemetry_revision", "INTEGER NOT NULL DEFAULT 0"),
            ("track_projection_revision", "INTEGER NOT NULL DEFAULT 0"));
        await EnsureColumnsAsync("track", ("points_revision", "INTEGER NOT NULL DEFAULT 0"));

        await connection.ExecuteAsync(
            "UPDATE session SET processed_telemetry_revision = 1 WHERE processed_telemetry_revision = 0 AND data IS NOT NULL");
        await connection.ExecuteAsync(
            "UPDATE session SET track_projection_revision = 1 WHERE track_projection_revision = 0 AND track IS NOT NULL");
        await connection.ExecuteAsync(
            "UPDATE track SET points_revision = 1 WHERE points_revision = 0 AND points IS NOT NULL");

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
              OR NEW.track_projection_revision IS NOT (CASE WHEN NEW.track IS NOT NULL THEN 1 ELSE 0 END)
            BEGIN
                UPDATE session
                SET processed_telemetry_revision = CASE WHEN NEW.data IS NOT NULL THEN 1 ELSE 0 END,
                    track_projection_revision = CASE WHEN NEW.track IS NOT NULL THEN 1 ELSE 0 END
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
                    THEN OLD.processed_telemetry_revision + 1
                    ELSE OLD.processed_telemetry_revision
                END
                WHERE id = NEW.id;
            END
            """);
        await connection.ExecuteAsync(
            """
            CREATE TRIGGER session_track_projection_revision_after_update
            AFTER UPDATE OF track, timestamp, duration_seconds, gps_offset_seconds, full_track_id ON session
            WHEN NEW.track IS NOT OLD.track
              OR NEW.timestamp IS NOT OLD.timestamp
              OR NEW.duration_seconds IS NOT OLD.duration_seconds
              OR NEW.gps_offset_seconds IS NOT OLD.gps_offset_seconds
              OR NEW.full_track_id IS NOT OLD.full_track_id
              OR NEW.track_projection_revision IS NOT OLD.track_projection_revision
            BEGIN
                UPDATE session
                SET track_projection_revision = CASE
                    WHEN NEW.track IS NOT OLD.track
                      OR NEW.timestamp IS NOT OLD.timestamp
                      OR NEW.duration_seconds IS NOT OLD.duration_seconds
                      OR NEW.gps_offset_seconds IS NOT OLD.gps_offset_seconds
                      OR NEW.full_track_id IS NOT OLD.full_track_id
                    THEN OLD.track_projection_revision + 1
                    ELSE OLD.track_projection_revision
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
                    WHEN NEW.points IS NOT OLD.points THEN OLD.points_revision + 1
                    ELSE OLD.points_revision
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
        var oneDayAgo = DateTimeOffset.Now.AddDays(-1).ToUnixTimeSeconds();

        var deletedSessions = await connection.Table<Session>().DeleteAsync(session => session.Deleted != null && session.Deleted < oneDayAgo);
        var duplicateTracks = await CleanupDuplicateTrackTimeRangesAsync();
        var deletedTracks = await connection.Table<Track>().DeleteAsync(track => track.Deleted != null && track.Deleted < oneDayAgo);
        var deletedBoards = await connection.Table<Board>().DeleteAsync(board => board.Deleted != null && board.Deleted < oneDayAgo);
        var deletedSetups = await connection.Table<Setup>().DeleteAsync(setup => setup.Deleted != null && setup.Deleted < oneDayAgo);
        var deletedBikes = await connection.Table<Bike>().DeleteAsync(bike => bike.Deleted != null && bike.Deleted < oneDayAgo);
        var deletedPairedDevices = await connection.Table<PairedDevice>().DeleteAsync(device => device.Expires < DateTime.UtcNow);

        return new CleanupSummary(
            deletedSessions,
            deletedTracks + duplicateTracks,
            deletedBoards,
            deletedSetups,
            deletedBikes,
            deletedPairedDevices);
    }

    private async Task<int> CleanupDuplicateTrackTimeRangesAsync()
    {
        var tracks = await connection.QueryAsync<TrackTimeRow>(
            """
            SELECT id, start_time, end_time, updated
            FROM track
            WHERE deleted IS NULL
            """);
        var duplicates = tracks
            .Where(track => track.StartTime <= track.EndTime)
            .GroupBy(track => (track.StartTime, track.EndTime))
            .Where(group => group.Count() > 1)
            .ToList();

        if (duplicates.Count == 0)
        {
            return 0;
        }

        var sessionReferences = await connection.QueryAsync<SessionTrackReferenceRow>(
            """
            SELECT id, full_track_id
            FROM session
            WHERE deleted IS NULL AND full_track_id IS NOT NULL
            """);
        var sessionReferenceCounts = sessionReferences
            .Where(reference => reference.FullTrackId.HasValue)
            .CountBy(reference => reference.FullTrackId!.Value)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var removed = 0;
        foreach (var group in duplicates)
        {
            var canonicalTrack = SelectCanonicalDuplicateTrack(group, sessionReferenceCounts);
            foreach (var duplicateTrack in group.Where(track => track.Id != canonicalTrack.Id))
            {
                await connection.ExecuteAsync(
                    "UPDATE session SET full_track_id=?, track=NULL, updated=? WHERE deleted IS NULL AND full_track_id=?",
                    canonicalTrack.Id,
                    now,
                    duplicateTrack.Id);
                removed += await connection.ExecuteAsync(
                    "UPDATE track SET deleted=?, updated=? WHERE id=? AND deleted IS NULL",
                    now,
                    now,
                    duplicateTrack.Id);
            }
        }

        return removed;
    }

    private static TrackTimeRow SelectCanonicalDuplicateTrack(
        IEnumerable<TrackTimeRow> tracks,
        IReadOnlyDictionary<Guid, int> sessionReferenceCounts)
    {
        return tracks
            .OrderByDescending(track => sessionReferenceCounts.TryGetValue(track.Id, out var count) ? count : 0)
            .ThenBy(track => track.Updated == 0 ? long.MaxValue : track.Updated)
            .ThenBy(track => track.Id)
            .First();
    }

    private static readonly string[] SessionSummaryMetricColumnNames =
    [
        "duration_seconds",
        "distance_meters",
        "ascent_meters",
        "descent_meters"
    ];

    private static readonly string[] DampingSpeedCutoffColumnNames =
    [
        "front_compression_damping_cutoff_mm_per_second",
        "front_rebound_damping_cutoff_mm_per_second",
        "rear_compression_damping_cutoff_mm_per_second",
        "rear_rebound_damping_cutoff_mm_per_second"
    ];

    private static readonly string[] BikeDampingSpeedCutoffColumnNames = DampingSpeedCutoffColumnNames;

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

    private sealed class TrackTimeRow
    {
        [Column("id")]
        public Guid Id { get; set; }

        [Column("start_time")]
        public long StartTime { get; set; }

        [Column("end_time")]
        public long EndTime { get; set; }

        [Column("updated")]
        public long Updated { get; set; }
    }

    private sealed class SessionTrackReferenceRow
    {
        [Column("id")]
        public Guid Id { get; set; }

        [Column("full_track_id")]
        public Guid? FullTrackId { get; set; }
    }

    private sealed record CleanupSummary(
        int Sessions,
        int Tracks,
        int Boards,
        int Setups,
        int Bikes,
        int PairedDevices);
}
