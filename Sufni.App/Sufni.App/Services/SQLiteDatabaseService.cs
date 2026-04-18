using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.Models;
using Sufni.Telemetry;
using Serilog;

namespace Sufni.App.Services;

public class SqLiteDatabaseService : IDatabaseService
{
    private static readonly ILogger logger = Log.ForContext<SqLiteDatabaseService>();

    private Task Initialization { get; }
    private readonly SQLiteAsyncConnection connection;

    public SqLiteDatabaseService() : this(AppPaths.DatabasePath, createAppDirectories: true)
    {
    }

    internal SqLiteDatabaseService(string databasePath) : this(databasePath, createAppDirectories: false)
    {
    }

    private SqLiteDatabaseService(string databasePath, bool createAppDirectories)
    {
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
            await BackfillRearSuspensionKindAsync();

            var cleanupSummary = await Cleanup();
            logger.Information("SQLite database initialized at {DatabasePath}", AppPaths.DatabasePath);
            logger.Verbose(
                "SQLite startup cleanup removed {SessionCacheCount} session caches, {SessionCount} sessions, {TrackCount} tracks, {BoardCount} boards, {SetupCount} setups, {BikeCount} bikes, and {PairedDeviceCount} paired devices",
                cleanupSummary.SessionCaches,
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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "All persisted entity types are statically referenced in this table list.")]
    private async Task CreateTablesAsync()
    {
        await connection.CreateTablesAsync(CreateFlags.None,
        [
            typeof(Board),
            typeof(Setup),
            typeof(Bike),
            typeof(Session),
            typeof(SessionCache),
            typeof(Synchronization),
            typeof(PairedDevice),
            typeof(Track),
            typeof(AppSetting)
        ]);
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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Callers use persisted entity types that are statically rooted or flow through annotated generic parameters.")]
    private Task<int> DeleteEntityAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T entity) where T : new()
    {
        return connection.DeleteAsync(entity);
    }

    private sealed record CleanupSummary(
        int SessionCaches,
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
        var deletedSessions = await connection.Table<Session>().DeleteAsync(s => s.Deleted != null && s.Deleted < oneDayAgo);
        var deletedTracks = await connection.Table<Track>().DeleteAsync(t => t.Deleted != null && t.Deleted < oneDayAgo);
        var deletedBoards = await connection.Table<Board>().DeleteAsync(b => b.Deleted != null && b.Deleted < oneDayAgo);
        var deletedSetups = await connection.Table<Setup>().DeleteAsync(s => s.Deleted != null && s.Deleted < oneDayAgo);
        var deletedBikes = await connection.Table<Bike>().DeleteAsync(b => b.Deleted != null && b.Deleted < oneDayAgo);
        var deletedPairedDevices = await connection.Table<PairedDevice>().DeleteAsync(pd => pd.Expires < DateTime.UtcNow);

        return new CleanupSummary(
            deletedSessionCaches,
            deletedSessions,
            deletedTracks,
            deletedBoards,
            deletedSetups,
            deletedBikes,
            deletedPairedDevices);
    }

    public async Task<List<T>> GetAllAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() where T : Synchronizable, new()
    {
        await Initialization;
        return await Table<T>().Where(s => s.Deleted == null).ToListAsync();
    }

    public async Task<List<T>> GetChangedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(long since) where T : Synchronizable, new()
    {
        await Initialization;
        return await Table<T>()
            .Where(s => s.Updated > since || (s.Deleted != null && s.Deleted > since))
            .ToListAsync();
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

        const string query = """
                             SELECT
                                 id,
                                 name,
                                 setup_id,
                                 description,
                                 timestamp,
                                 full_track_id,
                                 front_springrate, front_hsc, front_lsc, front_lsr, front_hsr,
                                 rear_springrate, rear_hsc, rear_lsc, rear_lsr, rear_hsr,
                                 updated,
                                 CASE
                                    WHEN data IS NOT NULL THEN 1
                                    ELSE 0
                                 END AS has_data
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

        const string query = """
                             SELECT
                                 id,
                                 name,
                                 setup_id,
                                 description,
                                 timestamp,
                                 full_track_id,
                                 front_springrate, front_hsc, front_lsc, front_lsr, front_hsr,
                                 rear_springrate, rear_hsc, rear_lsc, rear_lsr, rear_hsr,
                                 updated,
                                 CASE
                                    WHEN data IS NOT NULL THEN 1
                                    ELSE 0
                                 END AS has_data
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

    public async Task<TelemetryData?> GetSessionPsstAsync(Guid id)
    {
        await Initialization;
        var sessions = await connection.QueryAsync<Session>(
            "SELECT data FROM session WHERE deleted IS null AND id = ?", id);
        return sessions.Count == 1 && sessions[0].ProcessedData is not null
            ? TelemetryData.FromBinary(sessions[0].ProcessedData)
            : null;
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
            var trackJson = session.Track is null ? null : AppJson.Serialize(session.Track);
            const string query = """
                                 UPDATE session
                                 SET
                                     name=?,
                                     setup_id=?,
                                     description=?,
                                     timestamp=?,
                                     full_track_id=?,
                                     track=COALESCE(?, track),
                                     data=COALESCE(?, data),
                                     front_springrate=?, front_hsc=?, front_lsc=?, front_lsr=?, front_hsr=?,
                                     rear_springrate=?, rear_hsc=?, rear_lsc=?, rear_lsr=?, rear_hsr=?,
                                     updated=?,
                                     deleted=NULL,
                                     has_data=CASE WHEN COALESCE(?, data) IS NOT NULL THEN 1 ELSE 0 END
                                 WHERE
                                     id=?
                                 """;
            await connection.ExecuteAsync(query,
                [
                    session.Name,
                    session.Setup,
                    session.Description,
                    session.Timestamp,
                    session.FullTrack,
                    trackJson,
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
                    session.ProcessedData,
                    session.Id]);
        }
        else
        {
            await InsertEntityAsync(session);
        }

        return session.Id;
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

        await connection.ExecuteAsync("UPDATE session SET data=?, has_data=1 WHERE id=?", [data, id]);
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

        var pointsJson = AppJson.Serialize(points);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await connection.ExecuteAsync("UPDATE session SET track=?, updated=? WHERE id=?", [pointsJson, now, id]);
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
        var track = await connection.Table<Track>()
            .Where(t => t.Deleted == null && t.StartTime <= session.Timestamp && session.Timestamp <= t.EndTime)
            .FirstOrDefaultAsync();
        if (track is null) return null;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await connection.ExecuteAsync("UPDATE session SET full_track_id=?, updated=? WHERE id=?", track.Id, now, session.Id);
        return track.Id;
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

    public async Task<List<PairedDevice>> GetPairedDevicesAsync()
    {
        await Initialization;
        return await connection.Table<PairedDevice>().ToListAsync();
    }

    public async Task<PairedDevice?> GetPairedDeviceAsync(string id)
    {
        await Initialization;

        return await connection.Table<PairedDevice>().Where(d => d.DeviceId == id).FirstOrDefaultAsync();
    }

    public async Task<PairedDevice?> GetPairedDeviceByTokenAsync(string token)
    {
        await Initialization;

        return await connection.Table<PairedDevice>().Where(d => d.Token == token).FirstOrDefaultAsync();
    }

    public async Task PutPairedDeviceAsync(PairedDevice device)
    {
        await Initialization;

        var existing = await connection.Table<PairedDevice>()
            .Where(t => t.DeviceId == device.DeviceId)
            .FirstOrDefaultAsync() is not null;
        if (existing)
        {
            await UpdateEntityAsync(device);
        }
        else
        {
            await InsertEntityAsync(device);
        }
    }

    public async Task DeletePairedDeviceAsync(string id)
    {
        await Initialization;
        var token = await connection.Table<PairedDevice>()
            .Where(t => t.DeviceId == id)
            .FirstOrDefaultAsync();
        if (token is not null)
        {
            await DeleteEntityAsync(token);
        }
    }

    public async Task<string?> GetAppSettingAsync(string key)
    {
        await Initialization;
        var setting = await connection.Table<AppSetting>()
            .Where(s => s.Key == key)
            .FirstOrDefaultAsync();
        return setting?.Value;
    }

    public async Task PutAppSettingAsync(string key, string value)
    {
        await Initialization;
        var existing = await connection.Table<AppSetting>()
            .Where(s => s.Key == key)
            .FirstOrDefaultAsync() is not null;
        var setting = new AppSetting { Key = key, Value = value };
        if (existing)
        {
            await connection.UpdateAsync(setting);
        }
        else
        {
            await connection.InsertAsync(setting);
        }
    }

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

        const string query = """
                             UPDATE session
                             SET
                                 name=?,
                                 setup_id=?,
                                 description=?,
                                 timestamp=?,
                                 full_track_id=?,
                                 track=?,
                                 front_springrate=?, front_hsc=?, front_lsc=?, front_lsr=?, front_hsr=?,
                                 rear_springrate=?, rear_hsc=?, rear_lsc=?, rear_lsr=?, rear_hsr=?,
                                 updated=?,
                                 client_updated=?,
                                 deleted=?
                             WHERE
                                 id=?
                             """;

        await connection.ExecuteAsync(query,
            [
                session.Name,
                session.Setup,
                session.Description,
                session.Timestamp,
                session.FullTrack,
                session.Track is null ? null : AppJson.Serialize(session.Track),
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
                session.ClientUpdated,
                session.Deleted,
                session.Id
            ]);
    }

    private async Task MergeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T entity) where T : Synchronizable, new()
    {
        await Initialization;

        var existing = await FindAsync<T>(entity.Id);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (existing is null)
        {
            entity.ClientUpdated = entity.Updated;
            entity.Updated = now;
            await InsertEntityAsync(entity);
            return;
        }

        var existingContentVersion = existing.ClientUpdated > 0
            ? existing.ClientUpdated
            : existing.Updated;

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

        entity.ClientUpdated = entity.Updated;
        entity.Updated = now;
        await UpdateEntityAsync(entity);
    }

    public async Task MergeAllAsync(SynchronizationData data)
    {
        await Initialization;

        await connection.ExecuteAsync("BEGIN TRANSACTION");

        try
        {
            foreach (var bike in data.Bikes) await MergeAsync(bike);
            foreach (var setup in data.Setups) await MergeAsync(setup);
            foreach (var board in data.Boards) await MergeAsync(board);
            foreach (var session in data.Sessions) await MergeAsync(session);
            foreach (var track in data.Tracks) await MergeAsync(track);

            await connection.ExecuteAsync("COMMIT");
        }
        catch
        {
            await connection.ExecuteAsync("ROLLBACK");
            throw;
        }
    }
}