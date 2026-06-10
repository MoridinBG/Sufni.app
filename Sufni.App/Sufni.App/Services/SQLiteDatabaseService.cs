using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.Telemetry;
using Serilog;
using Sufni.App.ExtensionHost.Models;
using Sufni.App.ExtensionHost.SessionDetails;

namespace Sufni.App.Services;

public class SqLiteDatabaseService : IDatabaseService
{
    private static readonly ILogger logger = Log.ForContext<SqLiteDatabaseService>();

    private Task Initialization => connectionContext.Initialization;
    private SQLiteAsyncConnection connection => connectionContext.Connection;
    private readonly SqliteConnectionContext connectionContext;
    private readonly ISessionTelemetryProcessor sessionTelemetryProcessor;
    private readonly IPairedDeviceRepository pairedDeviceRepository;
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository;
    private readonly ISessionCacheStore sessionCacheStore;
    private readonly ITrackRepository trackRepository;
    private readonly ISessionRepository sessionRepository;
    private readonly ISyncDataStore syncDataStore;

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
        : this(
            new SqliteConnectionContext(
                databasePath,
                createAppDirectories,
                extensionMigrators,
                extensionCascadeRuleProviders,
                extensionStateRefreshParticipantsProvider),
            sessionTelemetryProcessor)
    {
    }

    private SqLiteDatabaseService(
        string databasePath,
        bool createAppDirectories,
        IEnumerable<IExtensionDatabaseMigrator> extensionMigrators,
        IEnumerable<IExtensionCascadeRuleProvider> extensionCascadeRuleProviders,
        IEnumerable<IExtensionStateRefreshParticipant> extensionStateRefreshParticipants,
        ISessionTelemetryProcessor? sessionTelemetryProcessor = null)
        : this(
            new SqliteConnectionContext(
                databasePath,
                createAppDirectories,
                extensionMigrators,
                extensionCascadeRuleProviders,
                () => extensionStateRefreshParticipants.ToArray()),
            sessionTelemetryProcessor)
    {
    }

    internal SqLiteDatabaseService(
        SqliteConnectionContext connectionContext,
        ISessionTelemetryProcessor? sessionTelemetryProcessor = null)
    {
        this.connectionContext = connectionContext;
        this.sessionTelemetryProcessor = sessionTelemetryProcessor ?? new SessionTelemetryProcessor();
        pairedDeviceRepository = new PairedDeviceRepository(connectionContext);
        recordedSessionSourceRepository = new RecordedSessionSourceRepository(connectionContext);
        sessionCacheStore = new SessionCacheStore(connectionContext);
        trackRepository = new TrackRepository(connectionContext);
        sessionRepository = new SessionRepository(connectionContext, this.sessionTelemetryProcessor, trackRepository);
        syncDataStore = new SynchronizationMergeEngine(connectionContext, trackRepository);
    }

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

    private const string SessionHasDataProjection = """
                                                    CASE
                                                       WHEN data IS NOT NULL THEN 1
                                                       ELSE 0
                                                    END AS has_data
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

    public Task<List<Session>> GetSessionsAsync() =>
        sessionRepository.GetSessionsAsync();

    public Task<Session?> GetSessionAsync(Guid id) =>
        sessionRepository.GetSessionAsync(id);

    public Task<List<Guid>> GetIncompleteSessionIdsAsync() =>
        sessionRepository.GetIncompleteSessionIdsAsync();

    public Task<List<RecordedSessionSource>> GetRecordedSessionSourcesAsync() =>
        recordedSessionSourceRepository.GetRecordedSessionSourcesAsync();

    public Task<RecordedSessionSource?> GetRecordedSessionSourceAsync(Guid id) =>
        recordedSessionSourceRepository.GetRecordedSessionSourceAsync(id);

    public Task<List<Guid>> GetSessionIdsMissingRecordedSourceAsync() =>
        recordedSessionSourceRepository.GetSessionIdsMissingRecordedSourceAsync();

    public Task<TelemetryData?> GetSessionPsstAsync(Guid id) =>
        sessionRepository.GetSessionPsstAsync(id);

    public Task<byte[]?> GetSessionRawPsstAsync(Guid id) =>
        sessionRepository.GetSessionRawPsstAsync(id);

    public Task<List<TrackPoint>?> GetSessionTrackAsync(Guid id) =>
        sessionRepository.GetSessionTrackAsync(id);

    public Task<Guid> PutSessionAsync(Session session) =>
        sessionRepository.PutSessionAsync(session);

    public Task PutRecordedSessionSourceAsync(RecordedSessionSource source) =>
        recordedSessionSourceRepository.PutRecordedSessionSourceAsync(source);

    public Task DeleteRecordedSessionSourceAsync(Guid sessionId) =>
        recordedSessionSourceRepository.DeleteRecordedSessionSourceAsync(sessionId);

    public Task<Session> PutProcessedSessionAsync(Session session, Track? newFullTrack, RecordedSessionSource? source) =>
        sessionRepository.PutProcessedSessionAsync(session, newFullTrack, source);

    public Task<Session?> PutProcessedSessionIfUnchangedAsync(
        Session session,
        Track? newFullTrack,
        RecordedSessionSource? source,
        long baselineUpdated)
    {
        return sessionRepository.PutProcessedSessionIfUnchangedAsync(session, newFullTrack, source, baselineUpdated);
    }

    public Task<Guid?> FindTrackByTimeRangeAsync(long startTime, long endTime) =>
        trackRepository.FindTrackByTimeRangeAsync(startTime, endTime);

    public Task PatchSessionPsstAsync(Guid id, byte[] data) =>
        sessionRepository.PatchSessionPsstAsync(id, data);

    public Task PatchSessionTrackAsync(Guid id, List<TrackPoint> points) =>
        sessionRepository.PatchSessionTrackAsync(id, points);

    public Task<SessionCache?> GetSessionCacheAsync(Guid sessionId) =>
        sessionCacheStore.GetSessionCacheAsync(sessionId);

    public Task<Guid> PutSessionCacheAsync(SessionCache sessionCache) =>
        sessionCacheStore.PutSessionCacheAsync(sessionCache);

    public Task<Guid?> AssociateSessionWithTrackAsync(Guid sessionId) =>
        trackRepository.AssociateSessionWithTrackAsync(sessionId);

    public Task<SynchronizationData> GetSynchronizationDataAsync(long since) =>
        syncDataStore.GetSynchronizationDataAsync(since);

    public Task ApplyRemoteSynchronizationDataAsync(SynchronizationData data) =>
        syncDataStore.ApplyRemoteSynchronizationDataAsync(data);

    public Task<long> GetLastSyncTimeAsync(string? serverUrl) =>
        syncDataStore.GetLastSyncTimeAsync(serverUrl);

    public Task UpdateLastSyncTimeAsync(string? serverUrl) =>
        syncDataStore.UpdateLastSyncTimeAsync(serverUrl);

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

    public Task MergeAllAsync(SynchronizationData data) =>
        syncDataStore.MergeAllAsync(data);
}
