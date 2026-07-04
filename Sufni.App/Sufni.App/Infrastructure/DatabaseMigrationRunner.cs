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
    private readonly CoreMigrationStore coreMigrations = new(connection);

    internal async Task RunAsync()
    {
        try
        {
            await connection.EnableWriteAheadLoggingAsync();
            await CreateTablesAsync();
            await EnsureSessionProcessingFingerprintColumnAsync();
            await EnsureSessionSummaryMetricColumnsAsync();
            await EnsureSessionGpsOffsetColumnAsync();
            await EnsureBikeDampingSpeedCutoffColumnsAsync();
            await EnsureBikeRearSuspensionColumnAsync();
            await DropSessionCacheTableAsync();
            await coreMigrations.EnsureTableAsync();
            await connection.ExecuteAsync(SessionBlobSwapRequestStore.CreateTableSql);
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

    private async Task EnsureSessionProcessingFingerprintColumnAsync()
    {
        var columns = await connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(session)");
        if (columns.Any(column => column.Name == "session_processing_fingerprint"))
        {
            return;
        }

        await connection.ExecuteAsync("ALTER TABLE session ADD COLUMN session_processing_fingerprint TEXT");
    }

    private async Task EnsureSessionSummaryMetricColumnsAsync()
    {
        var columns = await connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(session)");
        var columnNames = columns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var columnName in SessionSummaryMetricColumnNames)
        {
            if (!columnNames.Contains(columnName))
            {
                await connection.ExecuteAsync($"ALTER TABLE session ADD COLUMN {columnName} REAL");
            }
        }
    }

    private async Task EnsureSessionGpsOffsetColumnAsync()
    {
        var columns = await connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(session)");
        var columnNames = columns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!columnNames.Contains("gps_offset_seconds"))
        {
            await connection.ExecuteAsync("ALTER TABLE session ADD COLUMN gps_offset_seconds REAL");
        }

        await connection.ExecuteAsync("UPDATE session SET gps_offset_seconds = 0 WHERE gps_offset_seconds IS NULL");
    }

    private async Task EnsureBikeDampingSpeedCutoffColumnsAsync()
    {
        var columns = await connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(bike)");
        var columnNames = columns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var columnName in BikeDampingSpeedCutoffColumnNames)
        {
            if (!columnNames.Contains(columnName))
            {
                await connection.ExecuteAsync($"ALTER TABLE bike ADD COLUMN {columnName} REAL");
            }

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
