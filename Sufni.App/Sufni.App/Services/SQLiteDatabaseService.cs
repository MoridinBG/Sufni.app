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

    private Task Initialization => connectionContext.Initialization;
    private SQLiteAsyncConnection connection => connectionContext.Connection;
    private readonly SqliteConnectionContext connectionContext;
    private readonly ISessionTelemetryProcessor sessionTelemetryProcessor;
    private readonly IPairedDeviceRepository pairedDeviceRepository;
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository;
    private readonly ISessionCacheStore sessionCacheStore;
    private readonly ITrackRepository trackRepository;
    private readonly ISessionRepository sessionRepository;

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
    }

    public async Task<IExtensionDatabaseSession> OpenSessionAsync(CancellationToken cancellationToken = default)
    {
        await Initialization.WaitAsync(cancellationToken);
        return new ExtensionDatabaseSession(connection, connectionContext.ExtensionTableCatalog);
    }

    internal async Task<SQLiteAsyncConnection> GetInitializedConnectionAsync(CancellationToken cancellationToken = default)
    {
        await Initialization.WaitAsync(cancellationToken);
        return connection;
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
            tracks.AddRange(await trackRepository.GetTracksByIdsAsync(relatedTrackIds));
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
