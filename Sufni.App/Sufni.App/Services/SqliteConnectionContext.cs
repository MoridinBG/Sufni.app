using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SQLite;
using Serilog;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.ExtensionHost.SessionDetails;
using Sufni.App.ExtensionHosting.Database;
using Sufni.App.Models;

namespace Sufni.App.Services;

internal sealed class SqliteConnectionContext
{
    private static readonly ILogger logger = Log.ForContext<SqliteConnectionContext>();

    private readonly string databasePath;
    private readonly ExtensionDatabaseMigratorRunner extensionMigratorRunner;
    private readonly ExtensionCascadeService extensionCascadeService;

    internal SqliteConnectionContext(
        string databasePath,
        bool createAppDirectories,
        IEnumerable<IExtensionDatabaseMigrator> extensionMigrators,
        IEnumerable<IExtensionCascadeRuleProvider> extensionCascadeRuleProviders,
        Func<IReadOnlyList<IExtensionStateRefreshParticipant>> extensionStateRefreshParticipantsProvider)
    {
        this.databasePath = databasePath;
        var extensionMigratorList = extensionMigrators.ToArray();
        var extensionCascadeRuleProviderList = extensionCascadeRuleProviders.ToArray();

        if (createAppDirectories)
        {
            AppPaths.CreateRequiredDirectories();
        }
        else
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        Connection = new SQLiteAsyncConnection(databasePath);
        ExtensionTableCatalog = ExtensionDatabaseTableCatalog.Create(extensionMigratorList);
        extensionMigratorRunner = new ExtensionDatabaseMigratorRunner(extensionMigratorList);
        extensionCascadeService = new ExtensionCascadeService(
            Connection,
            extensionMigratorList,
            extensionCascadeRuleProviderList,
            extensionStateRefreshParticipantsProvider);
        Initialization = Init();
    }

    internal SQLiteAsyncConnection Connection { get; }

    internal ExtensionDatabaseTableCatalog ExtensionTableCatalog { get; }

    internal Task Initialization { get; }

    internal async Task<SQLiteAsyncConnection> GetInitializedConnectionAsync(CancellationToken cancellationToken = default)
    {
        await Initialization.WaitAsync(cancellationToken);
        return Connection;
    }

    private async Task Init()
    {
        try
        {
            await Connection.EnableWriteAheadLoggingAsync();
            await CreateTablesAsync();
            await EnsureSessionProcessingFingerprintColumnAsync();
            await EnsureSessionSummaryMetricColumnsAsync();
            await EnsureBikeDampingSpeedCutoffColumnsAsync();
            await EnsureSessionCacheDampingSpeedCutoffColumnsAsync();
            await BackfillRearSuspensionKindAsync();
            await extensionMigratorRunner.RunAsync(Connection);

            var cleanupSummary = await Cleanup();
            await extensionCascadeService.RepairOrphansAsync(refreshExtensionState: false);
            logger.Information("SQLite database initialized at {DatabasePath}", databasePath);
            logger.Verbose(
                "SQLite startup cleanup removed {SessionCacheCount} session caches, {RecordedSessionSourceCount} recorded session sources, {SessionCount} sessions, {TrackCount} tracks, {BoardCount} boards, {SetupCount} setups, {BikeCount} bikes, and {PairedDeviceCount} paired devices",
                cleanupSummary.SessionCaches,
                cleanupSummary.RecordedSessionSources,
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
        await Connection.CreateTablesAsync(CreateFlags.None,
        [
            typeof(Board),
            typeof(Setup),
            typeof(Bike),
            typeof(Session),
            typeof(RecordedSessionSource),
            typeof(SessionCache),
            typeof(Synchronization),
            typeof(PairedDevice),
            typeof(Track)
        ]);
    }

    private async Task EnsureSessionProcessingFingerprintColumnAsync()
    {
        var columns = await Connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(session)");
        if (columns.Any(column => column.Name == "session_processing_fingerprint"))
        {
            return;
        }

        await Connection.ExecuteAsync("ALTER TABLE session ADD COLUMN session_processing_fingerprint TEXT");
    }

    private async Task EnsureSessionSummaryMetricColumnsAsync()
    {
        var columns = await Connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(session)");
        var columnNames = columns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var columnName in SessionSummaryMetricColumnNames)
        {
            if (!columnNames.Contains(columnName))
            {
                await Connection.ExecuteAsync($"ALTER TABLE session ADD COLUMN {columnName} REAL");
            }
        }
    }

    private async Task EnsureBikeDampingSpeedCutoffColumnsAsync()
    {
        var columns = await Connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(bike)");
        var columnNames = columns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var columnName in BikeDampingSpeedCutoffColumnNames)
        {
            if (!columnNames.Contains(columnName))
            {
                await Connection.ExecuteAsync($"ALTER TABLE bike ADD COLUMN {columnName} REAL");
            }

            await Connection.ExecuteAsync(
                $"UPDATE bike SET {columnName} = ? WHERE {columnName} IS NULL",
                DampingSpeedCutoffs.DefaultMmPerSecond);
        }
    }

    private async Task EnsureSessionCacheDampingSpeedCutoffColumnsAsync()
    {
        var columns = await Connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(session_cache)");
        var columnNames = columns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var columnName in DampingSpeedCutoffColumnNames)
        {
            if (!columnNames.Contains(columnName))
            {
                await Connection.ExecuteAsync($"ALTER TABLE session_cache ADD COLUMN {columnName} REAL");
            }

            await Connection.ExecuteAsync(
                $"UPDATE session_cache SET {columnName} = ? WHERE {columnName} IS NULL",
                DampingSpeedCutoffs.DefaultMmPerSecond);
        }
    }

    private Task<int> BackfillRearSuspensionKindAsync() => Connection.ExecuteAsync(
        "UPDATE bike SET rear_suspension_kind = ? WHERE linkage IS NOT NULL AND (rear_suspension_kind IS NULL OR rear_suspension_kind = ?)",
        [(int)RearSuspensionKind.Linkage, (int)RearSuspensionKind.None]);

    private async Task<CleanupSummary> Cleanup()
    {
        var oneDayAgo = DateTimeOffset.Now.AddDays(-1).ToUnixTimeSeconds();

        var cleanSessionCachesQuery = $"""
                                       DELETE FROM session_cache
                                       WHERE session_id IN (
                                           SELECT id
                                           FROM session
                                           WHERE deleted IS NOT NULL AND deleted < {oneDayAgo}
                                       )
                                       """;
        var deletedSessionCaches = await Connection.ExecuteAsync(cleanSessionCachesQuery);

        var cleanRecordedSourcesForPurgedSessionsQuery = $"""
                                                          DELETE FROM session_recording_source
                                                          WHERE session_id IN (
                                                              SELECT id
                                                              FROM session
                                                              WHERE deleted IS NOT NULL AND deleted < {oneDayAgo}
                                                          )
                                                          """;
        var deletedRecordedSources = await Connection.ExecuteAsync(cleanRecordedSourcesForPurgedSessionsQuery);
        var deletedSessions = await Connection.Table<Session>().DeleteAsync(session => session.Deleted != null && session.Deleted < oneDayAgo);
        deletedRecordedSources += await Connection.ExecuteAsync(
            "DELETE FROM session_recording_source WHERE session_id NOT IN (SELECT id FROM session)");
        var duplicateTracks = await CleanupDuplicateTrackTimeRangesAsync();
        var deletedTracks = await Connection.Table<Track>().DeleteAsync(track => track.Deleted != null && track.Deleted < oneDayAgo);
        var deletedBoards = await Connection.Table<Board>().DeleteAsync(board => board.Deleted != null && board.Deleted < oneDayAgo);
        var deletedSetups = await Connection.Table<Setup>().DeleteAsync(setup => setup.Deleted != null && setup.Deleted < oneDayAgo);
        var deletedBikes = await Connection.Table<Bike>().DeleteAsync(bike => bike.Deleted != null && bike.Deleted < oneDayAgo);
        var deletedPairedDevices = await Connection.Table<PairedDevice>().DeleteAsync(device => device.Expires < DateTime.UtcNow);

        return new CleanupSummary(
            deletedSessionCaches,
            deletedRecordedSources,
            deletedSessions,
            deletedTracks + duplicateTracks,
            deletedBoards,
            deletedSetups,
            deletedBikes,
            deletedPairedDevices);
    }

    private async Task<int> CleanupDuplicateTrackTimeRangesAsync()
    {
        var tracks = await Connection.QueryAsync<TrackTimeRow>(
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

        var sessionReferences = await Connection.QueryAsync<SessionTrackReferenceRow>(
            """
            SELECT id, full_track_id
            FROM session
            WHERE deleted IS NULL AND full_track_id IS NOT NULL
            """);
        var sessionReferenceCounts = sessionReferences
            .Where(reference => reference.FullTrackId.HasValue)
            .GroupBy(reference => reference.FullTrackId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var removed = 0;
        foreach (var group in duplicates)
        {
            var canonicalTrack = SelectCanonicalDuplicateTrack(group, sessionReferenceCounts);
            foreach (var duplicateTrack in group.Where(track => track.Id != canonicalTrack.Id))
            {
                await Connection.ExecuteAsync(
                    "UPDATE session SET full_track_id=?, track=NULL, updated=? WHERE deleted IS NULL AND full_track_id=?",
                    canonicalTrack.Id,
                    now,
                    duplicateTrack.Id);
                removed += await Connection.ExecuteAsync(
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
        int SessionCaches,
        int RecordedSessionSources,
        int Sessions,
        int Tracks,
        int Boards,
        int Setups,
        int Bikes,
        int PairedDevices);
}
