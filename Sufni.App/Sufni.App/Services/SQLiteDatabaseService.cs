using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.Telemetry;
using Serilog;
using Sufni.App.ExtensionHost.Models;
using Sufni.App.ExtensionHost.SessionDetails;
using Sufni.App.ExtensionHosting.Database;

namespace Sufni.App.Services;

public class SqLiteDatabaseService : IDatabaseService, IExtensionDatabaseConnection
{
    private static readonly ILogger logger = Log.ForContext<SqLiteDatabaseService>();

    private Task Initialization { get; }
    private readonly SQLiteAsyncConnection connection;
    private readonly ExtensionDatabaseTableCatalog extensionTableCatalog;
    private readonly ExtensionDatabaseMigratorRunner extensionMigratorRunner;
    private readonly ExtensionCascadeService extensionCascadeService;
    private readonly ISessionTelemetryProcessor sessionTelemetryProcessor;
    private readonly IPairedDeviceRepository pairedDeviceRepository;
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository;

    public SqLiteDatabaseService()
        : this(
            AppPaths.DatabasePath,
            createAppDirectories: true,
            extensionMigrators: [],
            extensionCascadeRuleProviders: [],
            extensionStateRefreshParticipants: [])
    {
    }

    public SqLiteDatabaseService(IEnumerable<IExtensionDatabaseMigrator> extensionMigrators)
        : this(
            AppPaths.DatabasePath,
            createAppDirectories: true,
            extensionMigrators,
            extensionCascadeRuleProviders: [],
            extensionStateRefreshParticipants: [])
    {
    }

    public SqLiteDatabaseService(
        IEnumerable<IExtensionDatabaseMigrator> extensionMigrators,
        IEnumerable<IExtensionCascadeRuleProvider> extensionCascadeRuleProviders,
        IEnumerable<IExtensionStateRefreshParticipant> extensionStateRefreshParticipants)
        : this(
            AppPaths.DatabasePath,
            createAppDirectories: true,
            extensionMigrators,
            extensionCascadeRuleProviders,
            extensionStateRefreshParticipants)
    {
    }

    internal SqLiteDatabaseService(string databasePath)
        : this(
            databasePath,
            createAppDirectories: false,
            extensionMigrators: [],
            extensionCascadeRuleProviders: [],
            extensionStateRefreshParticipants: [])
    {
    }

    internal SqLiteDatabaseService(string databasePath, IEnumerable<IExtensionDatabaseMigrator> extensionMigrators)
        : this(
            databasePath,
            createAppDirectories: false,
            extensionMigrators,
            extensionCascadeRuleProviders: [],
            extensionStateRefreshParticipants: [])
    {
    }

    internal SqLiteDatabaseService(
        string databasePath,
        IEnumerable<IExtensionDatabaseMigrator> extensionMigrators,
        IEnumerable<IExtensionCascadeRuleProvider> extensionCascadeRuleProviders,
        IEnumerable<IExtensionStateRefreshParticipant> extensionStateRefreshParticipants)
        : this(
            databasePath,
            createAppDirectories: false,
            extensionMigrators,
            extensionCascadeRuleProviders,
            extensionStateRefreshParticipants)
    {
    }

    internal SqLiteDatabaseService(
        string databasePath,
        bool createAppDirectories,
        IEnumerable<IExtensionDatabaseMigrator> extensionMigrators,
        IEnumerable<IExtensionCascadeRuleProvider> extensionCascadeRuleProviders,
        Func<IReadOnlyList<IExtensionStateRefreshParticipant>> extensionStateRefreshParticipantsProvider,
        ISessionTelemetryProcessor? sessionTelemetryProcessor = null)
    {
        var extensionMigratorList = extensionMigrators.ToArray();
        var extensionCascadeRuleProviderList = extensionCascadeRuleProviders.ToArray();
        this.sessionTelemetryProcessor = sessionTelemetryProcessor ?? new SessionTelemetryProcessor();
        pairedDeviceRepository = new PairedDeviceRepository(this);
        recordedSessionSourceRepository = new RecordedSessionSourceRepository(this);

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

        connection = new SQLiteAsyncConnection(databasePath);
        extensionTableCatalog = ExtensionDatabaseTableCatalog.Create(extensionMigratorList);
        extensionMigratorRunner = new ExtensionDatabaseMigratorRunner(extensionMigratorList);
        extensionCascadeService = new ExtensionCascadeService(
            connection,
            extensionMigratorList,
            extensionCascadeRuleProviderList,
            extensionStateRefreshParticipantsProvider);
        Initialization = Init();
    }

    private SqLiteDatabaseService(
        string databasePath,
        bool createAppDirectories,
        IEnumerable<IExtensionDatabaseMigrator> extensionMigrators,
        IEnumerable<IExtensionCascadeRuleProvider> extensionCascadeRuleProviders,
        IEnumerable<IExtensionStateRefreshParticipant> extensionStateRefreshParticipants,
        ISessionTelemetryProcessor? sessionTelemetryProcessor = null)
    {
        var extensionMigratorList = extensionMigrators.ToArray();
        var extensionCascadeRuleProviderList = extensionCascadeRuleProviders.ToArray();
        var extensionStateRefreshParticipantList = extensionStateRefreshParticipants.ToArray();
        this.sessionTelemetryProcessor = sessionTelemetryProcessor ?? new SessionTelemetryProcessor();
        pairedDeviceRepository = new PairedDeviceRepository(this);
        recordedSessionSourceRepository = new RecordedSessionSourceRepository(this);

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

        connection = new SQLiteAsyncConnection(databasePath);
        extensionTableCatalog = ExtensionDatabaseTableCatalog.Create(extensionMigratorList);
        extensionMigratorRunner = new ExtensionDatabaseMigratorRunner(extensionMigratorList);
        extensionCascadeService = new ExtensionCascadeService(
            connection,
            extensionMigratorList,
            extensionCascadeRuleProviderList,
            extensionStateRefreshParticipantList);
        Initialization = Init();
    }

    private async Task Init()
    {
        try
        {
            if (connection == null)
            {
                throw new Exception("Database connection failed!");
            }

            await connection.EnableWriteAheadLoggingAsync();
            await CreateTablesAsync();
            await EnsureSessionProcessingFingerprintColumnAsync();
            await EnsureSessionSummaryMetricColumnsAsync();
            await EnsureBikeDampingSpeedCutoffColumnsAsync();
            await EnsureSessionCacheDampingSpeedCutoffColumnsAsync();
            await BackfillRearSuspensionKindAsync();
            await extensionMigratorRunner.RunAsync(connection);

            var cleanupSummary = await Cleanup();
            await extensionCascadeService.RepairOrphansAsync(refreshExtensionState: false);
            logger.Information("SQLite database initialized at {DatabasePath}", AppPaths.DatabasePath);
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
            logger.Error(ex, "SQLite database initialization failed at {DatabasePath}", AppPaths.DatabasePath);
            throw;
        }
    }

    public async Task<IExtensionDatabaseSession> OpenSessionAsync(CancellationToken cancellationToken = default)
    {
        await Initialization.WaitAsync(cancellationToken);
        return new ExtensionDatabaseSession(connection, extensionTableCatalog);
    }

    internal async Task<SQLiteAsyncConnection> GetInitializedConnectionAsync(CancellationToken cancellationToken = default)
    {
        await Initialization.WaitAsync(cancellationToken);
        return connection;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "All persisted entity types are statically referenced in this table list.")]
    private async Task CreateTablesAsync()
    {
        await connection.CreateTablesAsync(CreateFlags.None,
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

    private async Task EnsureSessionCacheDampingSpeedCutoffColumnsAsync()
    {
        var columns = await connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(session_cache)");
        var columnNames = columns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var columnName in DampingSpeedCutoffColumnNames)
        {
            if (!columnNames.Contains(columnName))
            {
                await connection.ExecuteAsync($"ALTER TABLE session_cache ADD COLUMN {columnName} REAL");
            }

            await connection.ExecuteAsync(
                $"UPDATE session_cache SET {columnName} = ? WHERE {columnName} IS NULL",
                DampingSpeedCutoffs.DefaultMmPerSecond);
        }
    }

    private Task<int> BackfillRearSuspensionKindAsync() => connection.ExecuteAsync(
        "UPDATE bike SET rear_suspension_kind = ? WHERE linkage IS NOT NULL AND (rear_suspension_kind IS NULL OR rear_suspension_kind = ?)",
        [(int)RearSuspensionKind.Linkage, (int)RearSuspensionKind.None]);

    private AsyncTableQuery<T> Table<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() where T : new()
    {
        return connection.Table<T>();
    }

    private static string GetTableName<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() where T : new()
    {
        return typeof(T).GetCustomAttribute<TableAttribute>()?.Name
               ?? throw new InvalidOperationException($"Type {typeof(T).Name} is missing a SQLite table attribute.");
    }

    private async Task<bool> EntityExistsAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(Guid id)
        where T : Synchronizable, new()
    {
        var tableName = GetTableName<T>();
        var count = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {tableName} WHERE id = ?", id);
        return count > 0;
    }

    private async Task<T?> FindAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(object primaryKey) where T : new()
    {
        return await connection.FindAsync<T>(primaryKey);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Callers use persisted entity types that are statically rooted or flow through annotated generic parameters.")]
    private Task<int> InsertEntityAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T entity) where T : new()
    {
        ValidateEntityForPersistence(entity);
        return connection.InsertAsync(entity);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Callers use persisted entity types that are statically rooted or flow through annotated generic parameters.")]
    private Task<int> UpdateEntityAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T entity) where T : new()
    {
        ValidateEntityForPersistence(entity);
        return connection.UpdateAsync(entity);
    }

    private static void ValidateEntityForPersistence<T>(T entity) where T : new()
    {
        if (entity is Track { HasPoints: false } track && track.Deleted is null)
        {
            throw new InvalidOperationException("Track must contain at least one point.");
        }
    }

    private const string SessionProcessingFingerprintColumn = "session_processing_fingerprint";

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

    private const string SessionHasDataProjection = """
                                                    CASE
                                                       WHEN data IS NOT NULL THEN 1
                                                       ELSE 0
                                                    END AS has_data
                                                    """;

    private static readonly string ActiveSessionMetadataProjection = $"""
                                                                     id,
                                                                     name,
                                                                     setup_id,
                                                                     description,
                                                                     timestamp,
                                                                     duration_seconds,
                                                                     distance_meters,
                                                                     ascent_meters,
                                                                     descent_meters,
                                                                     full_track_id,
                                                                     {SessionProcessingFingerprintColumn},
                                                                     front_springrate, front_hsc, front_lsc, front_lsr, front_hsr,
                                                                     rear_springrate, rear_hsc, rear_lsc, rear_lsr, rear_hsr,
                                                                     updated,
                                                                     {SessionHasDataProjection}
                                                                     """;

    private static readonly string SessionSynchronizationProjection = $"""
                                                                      id,
                                                                      name,
                                                                      setup_id,
                                                                      description,
                                                                      timestamp,
                                                                      duration_seconds,
                                                                      distance_meters,
                                                                      ascent_meters,
                                                                      descent_meters,
                                                                      full_track_id,
                                                                      {SessionProcessingFingerprintColumn},
                                                                      track,
                                                                      front_springrate, front_hsc, front_lsc, front_lsr, front_hsr,
                                                                      rear_springrate, rear_hsc, rear_lsc, rear_lsr, rear_hsr,
                                                                      updated,
                                                                      client_updated,
                                                                      deleted,
                                                                      {SessionHasDataProjection}
                                                                      """;

    private const string ProcessedSessionUpdateAssignments = """
                                                             name=?,
                                                             setup_id=?,
                                                             description=?,
                                                             timestamp=?,
                                                             duration_seconds=?,
                                                             distance_meters=?,
                                                             ascent_meters=?,
                                                             descent_meters=?,
                                                             full_track_id=?,
                                                             session_processing_fingerprint=?,
                                                             track=?,
                                                             data=?,
                                                             front_springrate=?, front_hsc=?, front_lsc=?, front_lsr=?, front_hsr=?,
                                                             rear_springrate=?, rear_hsc=?, rear_lsc=?, rear_lsr=?, rear_hsr=?,
                                                             updated=?,
                                                             deleted=NULL
                                                             """;

    private const string SessionMetadataSaveUpdateAssignments = """
                                                                name=?,
                                                                setup_id=?,
                                                                description=?,
                                                                timestamp=?,
                                                                full_track_id=?,
                                                                session_processing_fingerprint=?,
                                                                track=COALESCE(?, track),
                                                                data=COALESCE(?, data),
                                                                front_springrate=?, front_hsc=?, front_lsc=?, front_lsr=?, front_hsr=?,
                                                                rear_springrate=?, rear_hsc=?, rear_lsc=?, rear_lsr=?, rear_hsr=?,
                                                                updated=?,
                                                                deleted=NULL
                                                                """;

    private static readonly string UpdateProcessedSessionSql = $"""
                                                                UPDATE session
                                                                SET
                                                                    {ProcessedSessionUpdateAssignments}
                                                                WHERE
                                                                    id=?
                                                                """;

    private static readonly string UpdateProcessedSessionIfUnchangedSql = $"""
                                                                           UPDATE session
                                                                           SET
                                                                               {ProcessedSessionUpdateAssignments}
                                                                           WHERE
                                                                               id=? AND updated=?
                                                                           """;

    private static readonly string UpdateSessionMetadataSaveSql = $"""
                                                                   UPDATE session
                                                                   SET
                                                                       {SessionMetadataSaveUpdateAssignments}
                                                                   WHERE
                                                                       id=?
                                                                   """;

    private const string RemoteSessionMetadataUpdateAssignments = """
                                                                  name=?,
                                                                  setup_id=?,
                                                                  description=?,
                                                                  timestamp=?,
                                                                  duration_seconds=?,
                                                                  distance_meters=?,
                                                                  ascent_meters=?,
                                                                  descent_meters=?,
                                                                  full_track_id=?,
                                                                  session_processing_fingerprint=?,
                                                                  track=?,
                                                                  front_springrate=?, front_hsc=?, front_lsc=?, front_lsr=?, front_hsr=?,
                                                                  rear_springrate=?, rear_hsc=?, rear_lsc=?, rear_lsr=?, rear_hsr=?,
                                                                  updated=?,
                                                                  client_updated=?,
                                                                  deleted=?
                                                                  """;

    private static readonly string UpdateRemoteSessionMetadataSql = $"""
                                                                     UPDATE session
                                                                     SET
                                                                         {RemoteSessionMetadataUpdateAssignments}
                                                                     WHERE
                                                                         id=?
                                                                     """;

    private static string? SerializeTrack(Session session) =>
        session.Track is null ? null : AppJson.Serialize(session.Track);

    private static object?[] CreateProcessedSessionUpdateValues(Session session) =>
    [
        session.Name,
        session.Setup,
        session.Description,
        session.Timestamp,
        session.DurationSeconds,
        session.DistanceMeters,
        session.AscentMeters,
        session.DescentMeters,
        session.FullTrack,
        session.ProcessingFingerprintJson,
        SerializeTrack(session),
        session.ProcessedData,
        session.FrontSpringRate,
        session.FrontHighSpeedCompression,
        session.FrontLowSpeedCompression,
        session.FrontLowSpeedRebound,
        session.FrontHighSpeedRebound,
        session.RearSpringRate,
        session.RearHighSpeedCompression,
        session.RearLowSpeedCompression,
        session.RearLowSpeedRebound,
        session.RearHighSpeedRebound,
        session.Updated
    ];

    private static object?[] CreateProcessedSessionUpdateValues(Session session, long baselineUpdated) =>
    [
        .. CreateProcessedSessionUpdateValues(session),
        session.Id,
        baselineUpdated
    ];

    private static object?[] CreateProcessedSessionUpdateValuesWithId(Session session) =>
    [
        .. CreateProcessedSessionUpdateValues(session),
        session.Id
    ];

    private static object?[] CreateSessionMetadataSaveUpdateValuesWithId(Session session) =>
    [
        session.Name,
        session.Setup,
        session.Description,
        session.Timestamp,
        session.FullTrack,
        session.ProcessingFingerprintJson,
        SerializeTrack(session),
        session.ProcessedData,
        session.FrontSpringRate,
        session.FrontHighSpeedCompression,
        session.FrontLowSpeedCompression,
        session.FrontLowSpeedRebound,
        session.FrontHighSpeedRebound,
        session.RearSpringRate,
        session.RearHighSpeedCompression,
        session.RearLowSpeedCompression,
        session.RearLowSpeedRebound,
        session.RearHighSpeedRebound,
        session.Updated,
        session.Id
    ];

    private static object?[] CreateRemoteSessionMetadataValues(
        Session session,
        long updated,
        long clientUpdated) =>
    [
        session.Name,
        session.Setup,
        session.Description,
        session.Timestamp,
        session.DurationSeconds,
        session.DistanceMeters,
        session.AscentMeters,
        session.DescentMeters,
        session.FullTrack,
        session.ProcessingFingerprintJson,
        SerializeTrack(session),
        session.FrontSpringRate,
        session.FrontHighSpeedCompression,
        session.FrontLowSpeedCompression,
        session.FrontLowSpeedRebound,
        session.FrontHighSpeedRebound,
        session.RearSpringRate,
        session.RearHighSpeedCompression,
        session.RearLowSpeedCompression,
        session.RearLowSpeedRebound,
        session.RearHighSpeedRebound,
        updated,
        clientUpdated,
        session.Deleted,
        session.Id
    ];

    private async Task ApplySessionSummaryMetricsAsync(Session session, Track? generatedFullTrack)
    {
        var durationSeconds = sessionTelemetryProcessor.ReadProcessedDurationSeconds(session.ProcessedData) ?? session.DurationSeconds;
        var points = await GetMetricTrackPointsAsync(session, generatedFullTrack, durationSeconds);
        var metrics = sessionTelemetryProcessor.ComputeSummaryMetrics(durationSeconds, points);

        session.DurationSeconds = metrics.DurationSeconds;
        session.DistanceMeters = metrics.DistanceMeters;
        session.AscentMeters = metrics.AscentMeters;
        session.DescentMeters = metrics.DescentMeters;
    }

    private async Task<IReadOnlyList<TrackPoint>?> GetMetricTrackPointsAsync(
        Session session,
        Track? generatedFullTrack,
        double? durationSeconds)
    {
        if (session.Track is { Count: > 0 })
        {
            return session.Track;
        }

        if (generatedFullTrack?.Points is { Count: > 0 } generatedPoints)
        {
            return generatedPoints;
        }

        return await TryGenerateSessionTrackFromFullTrackAsync(session.FullTrack, session.Timestamp, durationSeconds);
    }

    private async Task<List<TrackPoint>?> TryGenerateSessionTrackFromFullTrackAsync(
        Guid? fullTrackId,
        long? timestamp,
        double? durationSeconds)
    {
        if (!fullTrackId.HasValue ||
            !timestamp.HasValue ||
            durationSeconds is not { } duration ||
            !double.IsFinite(duration) ||
            duration <= 0)
        {
            return null;
        }

        var fullTrack = await connection.Table<Track>()
            .Where(track => track.Id == fullTrackId.Value && track.Deleted == null)
            .FirstOrDefaultAsync();
        if (fullTrack is null)
        {
            return null;
        }

        return sessionTelemetryProcessor.GenerateSessionTrackFromFullTrack(fullTrack, timestamp, durationSeconds);
    }

    private Task<int> UpdateProcessedSessionAsync(Session session)
    {
        return connection.ExecuteAsync(
            UpdateProcessedSessionSql,
            CreateProcessedSessionUpdateValuesWithId(session));
    }

    private Task<int> UpdateProcessedSessionIfUnchangedAsync(Session session, long baselineUpdated)
    {
        return connection.ExecuteAsync(
            UpdateProcessedSessionIfUnchangedSql,
            CreateProcessedSessionUpdateValues(session, baselineUpdated));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Callers use persisted entity types that are statically rooted or flow through annotated generic parameters.")]
    private Task<int> DeleteEntityAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T entity) where T : new()
    {
        return connection.DeleteAsync(entity);
    }

    private sealed class TableColumnInfo
    {
        [Column("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TrackIdRow
    {
        [Column("id")]
        public Guid Id { get; set; }
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
        var deletedSessionCaches = await connection.ExecuteAsync(cleanSessionCachesQuery);

        var cleanRecordedSourcesForPurgedSessionsQuery = $"""
                                                          DELETE FROM session_recording_source
                                                          WHERE session_id IN (
                                                              SELECT id
                                                              FROM session
                                                              WHERE deleted IS NOT NULL AND deleted < {oneDayAgo}
                                                          )
                                                          """;
        var deletedRecordedSources = await connection.ExecuteAsync(cleanRecordedSourcesForPurgedSessionsQuery);
        var deletedSessions = await connection.Table<Session>().DeleteAsync(s => s.Deleted != null && s.Deleted < oneDayAgo);
        deletedRecordedSources += await connection.ExecuteAsync(
            "DELETE FROM session_recording_source WHERE session_id NOT IN (SELECT id FROM session)");
        var duplicateTracks = await CleanupDuplicateTrackTimeRangesAsync();
        var deletedTracks = await connection.Table<Track>().DeleteAsync(t => t.Deleted != null && t.Deleted < oneDayAgo);
        var deletedBoards = await connection.Table<Board>().DeleteAsync(b => b.Deleted != null && b.Deleted < oneDayAgo);
        var deletedSetups = await connection.Table<Setup>().DeleteAsync(s => s.Deleted != null && s.Deleted < oneDayAgo);
        var deletedBikes = await connection.Table<Bike>().DeleteAsync(b => b.Deleted != null && b.Deleted < oneDayAgo);
        var deletedPairedDevices = await connection.Table<PairedDevice>().DeleteAsync(pd => pd.Expires < DateTime.UtcNow);

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
            .GroupBy(reference => reference.FullTrackId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

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

    public async Task<List<T>> GetAllAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() where T : Synchronizable, new()
    {
        await Initialization;
        return await Table<T>().Where(s => s.Deleted == null).ToListAsync();
    }

    public async Task<List<T>> GetChangedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(long since) where T : Synchronizable, new()
    {
        await Initialization;
        if (typeof(T) == typeof(Session))
        {
            return (List<T>)(object)await GetChangedSessionsAsync(since);
        }

        return await Table<T>()
            .Where(s => s.Updated > since || (s.Deleted != null && s.Deleted > since))
            .ToListAsync();
    }

    private Task<List<Session>> GetChangedSessionsAsync(long since)
    {
        var query = $"""
                     SELECT
                         {SessionSynchronizationProjection}
                     FROM
                         session
                     WHERE
                         updated > ? OR (deleted IS NOT NULL AND deleted > ?)
                     """;
        return connection.QueryAsync<Session>(query, since, since);
    }

    public async Task<T> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(Guid id) where T : Synchronizable, new()
    {
        await Initialization;

        return await Table<T>()
            .Where(s => s.Id == id && s.Deleted == null)
            .FirstOrDefaultAsync();
    }

    public async Task<Guid> PutAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T item) where T : Synchronizable, new()
    {
        await Initialization;

        var existing = await EntityExistsAsync<T>(item.Id);
        item.Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        item.Deleted = null;
        if (existing)
        {
            await UpdateEntityAsync(item);
        }
        else
        {
            await InsertEntityAsync(item);
        }

        return item.Id;
    }

    public async Task DeleteAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(Guid id) where T : Synchronizable, new()
    {
        await Initialization;
        var item = await Table<T>()
            .Where(s => s.Id == id)
            .FirstOrDefaultAsync();
        if (item is not null && item.Deleted is null)
        {
            item.Deleted = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await UpdateEntityAsync(item);
        }
    }

    public async Task DeleteAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T item) where T : Synchronizable, new()
    {
        await Initialization;
        var itemFromDatabase = await Table<T>()
            .Where(s => s.Id == item.Id)
            .FirstOrDefaultAsync();
        if (itemFromDatabase is not null && itemFromDatabase.Deleted is null)
        {
            itemFromDatabase.Deleted = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await UpdateEntityAsync(itemFromDatabase);
        }
    }

    public async Task<List<Session>> GetSessionsAsync()
    {
        await Initialization;

        var query = $"""
                     SELECT
                         {ActiveSessionMetadataProjection}
                     FROM
                         session
                     WHERE
                         deleted IS NULL
                     ORDER BY timestamp DESC
                     """;
        var sessions = await connection.QueryAsync<Session>(query);
        return sessions;
    }

    public async Task<Session?> GetSessionAsync(Guid id)
    {
        await Initialization;

        var query = $"""
                     SELECT
                         {ActiveSessionMetadataProjection}
                     FROM
                         session
                     WHERE
                         deleted IS NULL AND id = ?
                     """;
        var sessions = await connection.QueryAsync<Session>(query, id);
        return sessions.Count == 1 ? sessions[0] : null;
    }

    public async Task<List<Guid>> GetIncompleteSessionIdsAsync()
    {
        await Initialization;

        const string query = "SELECT id FROM session WHERE deleted IS null AND data IS null";
        return (await connection.QueryAsync<Session>(query)).Select(s => s.Id).ToList();
    }

    public Task<List<RecordedSessionSource>> GetRecordedSessionSourcesAsync() =>
        recordedSessionSourceRepository.GetRecordedSessionSourcesAsync();

    public Task<RecordedSessionSource?> GetRecordedSessionSourceAsync(Guid id) =>
        recordedSessionSourceRepository.GetRecordedSessionSourceAsync(id);

    public Task<List<Guid>> GetSessionIdsMissingRecordedSourceAsync() =>
        recordedSessionSourceRepository.GetSessionIdsMissingRecordedSourceAsync();

    public async Task<TelemetryData?> GetSessionPsstAsync(Guid id)
    {
        await Initialization;
        var sessions = await connection.QueryAsync<Session>(
            "SELECT data FROM session WHERE deleted IS null AND id = ?", id);
        if (sessions.Count != 1 || sessions[0].ProcessedData is not { } processedData)
        {
            return null;
        }

        return sessionTelemetryProcessor.ReadProcessedTelemetryData(processedData);
    }

    public async Task<byte[]?> GetSessionRawPsstAsync(Guid id)
    {
        await Initialization;
        var sessions = await connection.QueryAsync<Session>(
            "SELECT data FROM session WHERE deleted IS null AND id = ?", id);
        return sessions.Count == 1 ? sessions[0].ProcessedData : null;
    }

    public async Task<List<TrackPoint>?> GetSessionTrackAsync(Guid id)
    {
        await Initialization;
        var sessions = await connection.QueryAsync<Session>(
            "SELECT track FROM session WHERE deleted IS null AND id = ?", id);
        return sessions.Count == 1 ? sessions[0].Track : null;
    }

    public async Task<Guid> PutSessionAsync(Session session)
    {
        await Initialization;

        var existing = await EntityExistsAsync<Session>(session.Id);
        session.Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        session.Deleted = null;
        if (existing)
        {
            await connection.ExecuteAsync(UpdateSessionMetadataSaveSql, CreateSessionMetadataSaveUpdateValuesWithId(session));
        }
        else
        {
            await InsertEntityAsync(session);
        }

        return session.Id;
    }

    public Task PutRecordedSessionSourceAsync(RecordedSessionSource source) =>
        recordedSessionSourceRepository.PutRecordedSessionSourceAsync(source);

    public Task DeleteRecordedSessionSourceAsync(Guid sessionId) =>
        recordedSessionSourceRepository.DeleteRecordedSessionSourceAsync(sessionId);

    public async Task<Session> PutProcessedSessionAsync(Session session, Track? newFullTrack, RecordedSessionSource? source)
    {
        return await PutProcessedSessionCoreAsync(session, newFullTrack, source, baselineUpdated: null)
               ?? throw new InvalidOperationException($"Session {session.Id} was not found after processed-session persistence.");
    }

    public Task<Session?> PutProcessedSessionIfUnchangedAsync(
        Session session,
        Track? newFullTrack,
        RecordedSessionSource? source,
        long baselineUpdated)
    {
        return PutProcessedSessionCoreAsync(session, newFullTrack, source, baselineUpdated);
    }

    private async Task<Session?> PutProcessedSessionCoreAsync(
        Session session,
        Track? newFullTrack,
        RecordedSessionSource? source,
        long? baselineUpdated)
    {
        await Initialization;

        if (source is not null && source.SessionId != session.Id)
        {
            throw new InvalidOperationException("Recorded session source must belong to the processed session.");
        }

        await connection.ExecuteAsync("BEGIN TRANSACTION");

        try
        {
            if (newFullTrack is not null)
            {
                var existingTrack = await EntityExistsAsync<Track>(newFullTrack.Id);
                newFullTrack.Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                newFullTrack.Deleted = null;

                if (existingTrack)
                {
                    await UpdateEntityAsync(newFullTrack);
                }
                else
                {
                    await InsertEntityAsync(newFullTrack);
                }

                session.FullTrack = newFullTrack.Id;
            }
            else if (session.FullTrack is null && session.Timestamp.HasValue)
            {
                session.FullTrack = await FindTrackContainingTimestampAsync(session.Timestamp.Value);
            }

            await ApplySessionSummaryMetricsAsync(session, newFullTrack);

            var existingSession = await EntityExistsAsync<Session>(session.Id);
            session.Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            session.Deleted = null;

            if (existingSession)
            {
                var updatedRows = baselineUpdated.HasValue
                    ? await UpdateProcessedSessionIfUnchangedAsync(session, baselineUpdated.Value)
                    : await UpdateProcessedSessionAsync(session);
                if (updatedRows == 0)
                {
                    await connection.ExecuteAsync("ROLLBACK");
                    return null;
                }
            }
            else
            {
                if (baselineUpdated.HasValue)
                {
                    await connection.ExecuteAsync("ROLLBACK");
                    return null;
                }

                await InsertEntityAsync(session);
            }

                if (source is not null)
                {
                    await RecordedSessionSourceRepository.PutRecordedSessionSourceInCurrentTransactionAsync(
                        connection,
                        source);
                }

            await connection.ExecuteAsync("COMMIT");
        }
        catch
        {
            await connection.ExecuteAsync("ROLLBACK");
            throw;
        }

        return await GetSessionAsync(session.Id)
               ?? throw new InvalidOperationException($"Session {session.Id} was not found after processed-session persistence.");
    }

    public async Task<Guid?> FindTrackByTimeRangeAsync(long startTime, long endTime)
    {
        await Initialization;

        var rows = await connection.QueryAsync<TrackIdRow>(
            """
            SELECT id
            FROM track
            WHERE deleted IS NULL AND start_time = ? AND end_time = ?
            ORDER BY updated ASC, id ASC
            LIMIT 1
            """,
            startTime,
            endTime);
        return rows.Count == 0 ? null : rows[0].Id;
    }

    public async Task PatchSessionPsstAsync(Guid id, byte[] data)
    {
        await Initialization;

        var session = await connection.Table<Session>()
            .Where(s => s.Id == id && s.Deleted == null)
            .FirstOrDefaultAsync();
        if (session is null)
        {
            throw new Exception($"Session {id} does not exist.");
        }

        var telemetryData = sessionTelemetryProcessor.ReadProcessedTelemetryData(data);
        session.ProcessedData = data;
        var durationSeconds = telemetryData.Metadata?.Duration ?? session.DurationSeconds;
        var metrics = sessionTelemetryProcessor.ComputeSummaryMetrics(durationSeconds, session.Track);
        var hasTrackPoints = session.Track is { Count: > 0 };

        await connection.ExecuteAsync(
            """
            UPDATE session
            SET
                data=?,
                duration_seconds=?,
                distance_meters=?,
                ascent_meters=?,
                descent_meters=?
            WHERE id=?
            """,
            data,
            metrics.DurationSeconds,
            hasTrackPoints ? metrics.DistanceMeters : session.DistanceMeters,
            hasTrackPoints ? metrics.AscentMeters : session.AscentMeters,
            hasTrackPoints ? metrics.DescentMeters : session.DescentMeters,
            id);
    }

    public async Task PatchSessionTrackAsync(Guid id, List<TrackPoint> points)
    {
        await Initialization;

        var session = await connection.Table<Session>()
            .Where(s => s.Id == id && s.Deleted == null)
            .FirstOrDefaultAsync();
        if (session is null)
        {
            throw new Exception($"Session {id} does not exist.");
        }

        session.Track = points;
        var metrics = sessionTelemetryProcessor.ComputeSummaryMetrics(
            sessionTelemetryProcessor.ReadProcessedDurationSeconds(session.ProcessedData) ?? session.DurationSeconds,
            points);
        var pointsJson = AppJson.Serialize(points);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await connection.ExecuteAsync(
            """
            UPDATE session
            SET
                track=?,
                duration_seconds=?,
                distance_meters=?,
                ascent_meters=?,
                descent_meters=?,
                updated=?
            WHERE id=?
            """,
            pointsJson,
            metrics.DurationSeconds,
            metrics.DistanceMeters,
            metrics.AscentMeters,
            metrics.DescentMeters,
            now,
            id);
    }

    public async Task<SessionCache?> GetSessionCacheAsync(Guid sessionId)
    {
        await Initialization;
        return await connection.Table<SessionCache>()
            .Where(s => s.SessionId == sessionId)
            .FirstOrDefaultAsync();
    }

    public async Task<Guid> PutSessionCacheAsync(SessionCache sessionCache)
    {
        await Initialization;

        var existing = await connection.Table<SessionCache>()
            .Where(s => s.SessionId == sessionCache.SessionId)
            .FirstOrDefaultAsync() is not null;
        if (existing)
        {
            await UpdateEntityAsync(sessionCache);
        }
        else
        {
            await InsertEntityAsync(sessionCache);
        }

        return sessionCache.SessionId;
    }

    public async Task<Guid?> AssociateSessionWithTrackAsync(Guid sessionId)
    {
        await Initialization;

        var sessions = await connection.QueryAsync<Session>(
            "SELECT id,timestamp FROM session WHERE deleted IS null AND id = ?", sessionId);
        if (sessions.Count == 0)
        {
            throw new Exception($"Session {sessionId} does not exist.");
        }

        var session = sessions[0];
        var trackId = await FindTrackContainingTimestampAsync(session.Timestamp);
        if (trackId is null) return null;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await connection.ExecuteAsync("UPDATE session SET full_track_id=?, updated=? WHERE id=?", trackId.Value, now, session.Id);
        return trackId;
    }

    private async Task<Guid?> FindTrackContainingTimestampAsync(long? timestamp)
    {
        if (!timestamp.HasValue)
        {
            return null;
        }

        var rows = await connection.QueryAsync<TrackIdRow>(
            """
            SELECT id
            FROM track
            WHERE deleted IS NULL AND start_time <= ? AND ? <= end_time
            ORDER BY start_time DESC, end_time ASC, updated ASC, id ASC
            LIMIT 1
            """,
            timestamp.Value,
            timestamp.Value);
        return rows.Count == 0 ? null : rows[0].Id;
    }

    public async Task<SynchronizationData> GetSynchronizationDataAsync(long since)
    {
        await Initialization;

        var boards = await GetChangedAsync<Board>(since);
        var bikes = await GetChangedAsync<Bike>(since);
        var setups = await GetChangedAsync<Setup>(since);
        var sessions = await GetChangedAsync<Session>(since);
        var tracks = await GetChangedAsync<Track>(since);

        var changedTrackIds = tracks.Select(track => track.Id).ToHashSet();
        var relatedTrackIds = sessions
            .Where(session => session.Deleted is null && session.FullTrack.HasValue)
            .Select(session => session.FullTrack!.Value)
            .Where(trackId => !changedTrackIds.Contains(trackId))
            .Distinct()
            .ToList();

        if (relatedTrackIds.Count > 0)
        {
            tracks.AddRange(await GetTracksByIdsAsync(relatedTrackIds));
        }

        return new SynchronizationData
        {
            Boards = boards,
            Bikes = bikes,
            Setups = setups,
            Sessions = sessions,
            Tracks = tracks
        };
    }

    public async Task ApplyRemoteSynchronizationDataAsync(SynchronizationData data)
    {
        await Initialization;

        await connection.ExecuteAsync("BEGIN TRANSACTION");

        try
        {
            foreach (var board in data.Boards) await ApplyRemoteEntityAsync(board);
            foreach (var bike in data.Bikes) await ApplyRemoteEntityAsync(bike);
            foreach (var setup in data.Setups) await ApplyRemoteEntityAsync(setup);
            foreach (var track in data.Tracks) await ApplyRemoteEntityAsync(track);
            foreach (var session in data.Sessions) await ApplyRemoteSessionAsync(session);

            await connection.ExecuteAsync("COMMIT");
        }
        catch
        {
            await connection.ExecuteAsync("ROLLBACK");
            throw;
        }
    }

    public async Task<long> GetLastSyncTimeAsync(string? serverUrl)
    {
        await Initialization;

        var s = await connection.Table<Synchronization>()
            .Where(s => s.ServerUrl == serverUrl)
            .FirstOrDefaultAsync();
        return s?.LastSyncTime ?? 0;
    }

    public async Task UpdateLastSyncTimeAsync(string? serverUrl)
    {
        await Initialization;

        var lastSyncTime = DateTimeOffset.Now.ToUnixTimeSeconds();
        var synchronization = await connection.Table<Synchronization>()
            .Where(s => s.ServerUrl == serverUrl)
            .FirstOrDefaultAsync();

        if (synchronization is null)
        {
            await connection.InsertAsync(new Synchronization
            {
                ServerUrl = serverUrl,
                LastSyncTime = lastSyncTime
            });
            return;
        }

        synchronization.LastSyncTime = lastSyncTime;
        await connection.UpdateAsync(synchronization);
    }

    public Task<List<PairedDevice>> GetPairedDevicesAsync() =>
        pairedDeviceRepository.GetPairedDevicesAsync();

    public Task<PairedDevice?> GetPairedDeviceAsync(string id) =>
        pairedDeviceRepository.GetPairedDeviceAsync(id);

    public Task<PairedDevice?> GetPairedDeviceByTokenAsync(string token) =>
        pairedDeviceRepository.GetPairedDeviceByTokenAsync(token);

    public Task PutPairedDeviceAsync(PairedDevice device) =>
        pairedDeviceRepository.PutPairedDeviceAsync(device);

    public Task DeletePairedDeviceAsync(string id) =>
        pairedDeviceRepository.DeletePairedDeviceAsync(id);

    private async Task<List<Track>> GetTracksByIdsAsync(IReadOnlyCollection<Guid> trackIds)
    {
        var tracks = new List<Track>(trackIds.Count);

        foreach (var trackId in trackIds)
        {
            var track = await GetAsync<Track>(trackId);
            if (track is not null)
            {
                tracks.Add(track);
            }
        }

        return tracks;
    }

    private async Task ApplyRemoteEntityAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T entity)
        where T : Synchronizable, new()
    {
        var existing = await FindAsync<T>(entity.Id);
        if (existing is null)
        {
            await InsertEntityAsync(entity);
            return;
        }

        await UpdateEntityAsync(entity);
    }

    private async Task ApplyRemoteSessionAsync(Session session)
    {
        var existing = await FindAsync<Session>(session.Id);
        if (existing is null)
        {
            await InsertEntityAsync(session);
            return;
        }

        await connection.ExecuteAsync(
            UpdateRemoteSessionMetadataSql,
            CreateRemoteSessionMetadataValues(session, session.Updated, session.ClientUpdated));
    }

    private static long GetContentVersion(Synchronizable entity) => entity.ClientUpdated > 0
        ? entity.ClientUpdated
        : entity.Updated;

    private async Task MergeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T entity,
        Func<T, long, bool, Task> applyAcceptedContentAsync) where T : Synchronizable, new()
    {
        await Initialization;

        var existing = await FindAsync<T>(entity.Id);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (existing is null)
        {
            await applyAcceptedContentAsync(entity, now, true);
            return;
        }

        var existingContentVersion = GetContentVersion(existing);

        if (existing.Deleted.HasValue)
        {
            if (entity.Deleted.HasValue && entity.Deleted > existing.Deleted)
            {
                existing.Deleted = entity.Deleted;
            }

            existing.Updated = now;
            await UpdateEntityAsync(existing);
            return;
        }

        if (entity.Deleted.HasValue)
        {
            if (entity.Deleted <= existingContentVersion)
            {
                existing.Updated = now;
                await UpdateEntityAsync(existing);
                return;
            }

            existing.Deleted = entity.Deleted;
            existing.Updated = now;
            await UpdateEntityAsync(existing);
            return;
        }

        // Some other client updated the row  later and synced earlier. We
        // want the latest update, so discard content in this update, but
        // adjust update timestamp.
        if (existingContentVersion > entity.Updated)
        {
            existing.Updated = now;
            await UpdateEntityAsync(existing);
            return;
        }

        await applyAcceptedContentAsync(entity, now, false);
    }

    private Task PersistAcceptedEntityAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T entity,
        long now,
        bool isInsert) where T : Synchronizable, new()
    {
        return PersistEntityWithServerTimestampsAsync(
            entity,
            updated: now,
            clientUpdated: entity.Updated,
            persistAsync: isInsert ? InsertEntityAsync : UpdateEntityAsync);
    }

    private async Task PersistEntityWithServerTimestampsAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T entity,
        long updated,
        long clientUpdated,
        Func<T, Task<int>> persistAsync) where T : Synchronizable, new()
    {
        var originalUpdated = entity.Updated;
        var originalClientUpdated = entity.ClientUpdated;

        try
        {
            entity.Updated = updated;
            entity.ClientUpdated = clientUpdated;
            await persistAsync(entity);
        }
        finally
        {
            entity.Updated = originalUpdated;
            entity.ClientUpdated = originalClientUpdated;
        }
    }

    private Task MergeGenericAcceptedContentAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T entity,
        long now,
        bool isInsert) where T : Synchronizable, new()
    {
        return PersistAcceptedEntityAsync(entity, now, isInsert);
    }

    private Task MergeSessionMetadataAsync(Session session, long now)
    {
        return connection.ExecuteAsync(
            UpdateRemoteSessionMetadataSql,
            CreateRemoteSessionMetadataValues(session, now, session.Updated));
    }

    private Task MergeSessionAcceptedContentAsync(Session session, long now, bool isInsert) =>
        isInsert
            ? PersistAcceptedEntityAsync(session, now, isInsert: true)
            : MergeSessionMetadataAsync(session, now);

    public async Task MergeAllAsync(SynchronizationData data)
    {
        await Initialization;

        await connection.ExecuteAsync("BEGIN TRANSACTION");

        try
        {
            foreach (var bike in data.Bikes) await MergeAsync(bike, MergeGenericAcceptedContentAsync);
            foreach (var setup in data.Setups) await MergeAsync(setup, MergeGenericAcceptedContentAsync);
            foreach (var board in data.Boards) await MergeAsync(board, MergeGenericAcceptedContentAsync);
            foreach (var session in data.Sessions) await MergeAsync(session, MergeSessionAcceptedContentAsync);
            foreach (var track in data.Tracks) await MergeAsync(track, MergeGenericAcceptedContentAsync);

            await connection.ExecuteAsync("COMMIT");
        }
        catch
        {
            await connection.ExecuteAsync("ROLLBACK");
            throw;
        }
    }
}
