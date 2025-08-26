using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Services;

public class SqLiteDatabaseService : IDatabaseService
{
    private Task Initialization { get; }
    private readonly SQLiteAsyncConnection connection;

    public SqLiteDatabaseService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sufni.App");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        connection = new SQLiteAsyncConnection(Path.Combine(dir, "sst.db"));
        Initialization = Init();
    }

    private async Task Init()
    {
        if (connection == null)
        {
            throw new Exception("Database connection failed!");
        }

        await connection.EnableWriteAheadLoggingAsync();
        var result = await connection.CreateTablesAsync(CreateFlags.None,
        [
            typeof(Board),
            typeof(Setup),
            typeof(Bike),
            typeof(Session),
            typeof(SessionCache),
            typeof(Synchronization),
            typeof(PairedDevice),
            typeof(Track)
        ]);

        if (result.Results[typeof(Synchronization)] == CreateTableResult.Created)
        {
            await connection.QueryAsync<Synchronization>("INSERT INTO sync VALUES (0)");
        }

        await Cleanup();
    }

    private async Task Cleanup()
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
        await connection.ExecuteAsync(cleanSessionCachesQuery);
        await connection.Table<Session>().DeleteAsync(s => s.Deleted != null && s.Deleted < oneDayAgo);
        await connection.Table<Board>().DeleteAsync(b => b.Deleted != null && b.Deleted < oneDayAgo);
        await connection.Table<Setup>().DeleteAsync(s => s.Deleted != null && s.Deleted < oneDayAgo);
        await connection.Table<Bike>().DeleteAsync(b => b.Deleted != null && b.Deleted < oneDayAgo);
        await connection.Table<PairedDevice>().DeleteAsync(pd => pd.Expires < DateTime.UtcNow);
    }

    public async Task<List<T>> GetAllAsync<T>() where T : Synchronizable, new()
    {
        await Initialization;
        return await connection.Table<T>().Where(s => s.Deleted == null).ToListAsync();
    }

    public async Task<List<T>> GetChangedAsync<T>(long since) where T : Synchronizable, new()
    {
        await Initialization;
        return await connection.Table<T>()
            .Where(s => s.Updated > since || (s.Deleted != null && s.Deleted > since))
            .ToListAsync();
    }

    public async Task<T> GetAsync<T>(Guid id) where T : Synchronizable, new()
    {
        await Initialization;

        return await connection.Table<T>()
            .Where(s => s.Id == id && s.Deleted == null)
            .FirstOrDefaultAsync();
    }

    public async Task<Guid> PutAsync<T>(T item) where T : Synchronizable, new()
    {
        await Initialization;

        var existing = await connection.Table<T>()
            .Where(s => s.Id == item.Id && s.Deleted == null)
            .FirstOrDefaultAsync() is not null;
        item.Updated = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
        if (existing)
        {
            await connection.UpdateAsync(item);
        }
        else
        {
            await connection.InsertAsync(item);
        }

        return item.Id;
    }

    public async Task DeleteAsync<T>(Guid id) where T : Synchronizable, new()
    {
        await Initialization;
        var item = await connection.Table<T>()
            .Where(s => s.Id == id)
            .FirstOrDefaultAsync();
        if (item is not null)
        {
            item.Deleted = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
            await connection.UpdateAsync(item);
        }
    }

    public async Task DeleteAsync<T>(T item) where T : Synchronizable, new()
    {
        await Initialization;
        var itemFromDatabase = await connection.Table<T>()
            .Where(s => s.Id == item.Id)
            .FirstOrDefaultAsync();
        if (itemFromDatabase is not null)
        {
            itemFromDatabase.Deleted = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
            await connection.UpdateAsync(itemFromDatabase);
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
                                 track_id,
                                 front_springrate, front_hsc, front_lsc, front_lsr, front_hsr,
                                 rear_springrate, rear_hsc, rear_lsc, rear_lsr, rear_hsr,
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
        return sessions.Count == 1 ? TelemetryData.FromBinary(sessions[0].ProcessedData) : null;
    }

    public async Task<byte[]?> GetSessionRawPsstAsync(Guid id)
    {
        await Initialization;
        var sessions = await connection.QueryAsync<Session>(
            "SELECT data FROM session WHERE deleted IS null AND id = ?", id);
        return sessions.Count == 1 ? sessions[0].ProcessedData : null;
    }

    public async Task<Guid> PutSessionAsync(Session session)
    {
        await Initialization;

        var existing = await connection.Table<Session>()
            .Where(s => s.Id == session.Id && s.Deleted == null)
            .FirstOrDefaultAsync() is not null;
        session.Updated = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
        if (existing)
        {
            const string query = """
                                 UPDATE session
                                 SET
                                     name=?,
                                     description=?,
                                     front_springrate=?, front_hsc=?, front_lsc=?, front_lsr=?, front_hsr=?,
                                     rear_springrate=?, rear_hsc=?, rear_lsc=?, rear_lsr=?, rear_hsr=?
                                 WHERE
                                     id=?
                                 """;
            await connection.ExecuteAsync(query,
                [
                    session.Name,
                    session.Description,
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
                    session.Id]);
        }
        else
        {
            await connection.InsertAsync(session);
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

        await connection.ExecuteAsync("UPDATE session SET data=? WHERE id=?", [data, id]);
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
            await connection.UpdateAsync(sessionCache);
        }
        else
        {
            await connection.InsertAsync(sessionCache);
        }

        return sessionCache.SessionId;
    }

    public async Task<long> GetLastSyncTimeAsync()
    {
        await Initialization;

        var s = await connection.Table<Synchronization>().FirstOrDefaultAsync();
        return s?.LastSyncTime ?? 0;
    }

    public async Task UpdateLastSyncTimeAsync()
    {
        await Initialization;

        await connection.QueryAsync<Synchronization>("UPDATE sync SET last_sync_time = ?",
            (int)DateTimeOffset.Now.ToUnixTimeSeconds());
    }

    public async Task<List<PairedDevice>> GetPairedDevicesAsync()
    {
        await Initialization;
        return await connection.Table<PairedDevice>().ToListAsync();
    }

    public async Task<PairedDevice?> GetPairedDeviceAsync(string token)
    {
        await Initialization;

        return await connection.Table<PairedDevice>().Where(r => r.Token == token).FirstOrDefaultAsync();
    }
    
    public async Task PutPairedDeviceAsync(PairedDevice token)
    {
        await Initialization;

        var existing = await connection.Table<PairedDevice>()
            .Where(t => t.DeviceId == token.DeviceId)
            .FirstOrDefaultAsync() is not null;
        if (existing)
        {
            await connection.UpdateAsync(token);
        }
        else
        {
            await connection.InsertAsync(token);
        }
    }

    public async Task DeletePairedDeviceAsync(string deviceId)
    {
        await Initialization;
        var token = await connection.Table<PairedDevice>()
            .Where(t => t.DeviceId == deviceId)
            .FirstOrDefaultAsync();
        if (token is not null)
        {
            await connection.DeleteAsync(token);
        }
    }

    private async Task MergeAsync<T>(T entity) where T : Synchronizable, new()
    {
        await Initialization;

        var existing = await connection.FindAsync<T>(entity.Id);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (existing is null)
        {
            entity.ClientUpdated = entity.Updated;
            entity.Updated = now;
            await connection.InsertAsync(entity);
            return;
        }

        if (entity.Deleted.HasValue)
        {
            existing.Deleted = entity.Deleted;
            existing.Updated = entity.Updated;
            await connection.UpdateAsync(existing);
            return;
        }

        // Some other client updated the row  later and synced earlier. We
        // want the latest update, so discard content in this update, but
        // adjust update timestamp.
        if (existing.ClientUpdated > entity.Updated)
        {
            existing.Updated = now;
            await connection.UpdateAsync(existing);
            return;
        }

        entity.ClientUpdated = entity.Updated;
        entity.Updated = now;
        await connection.UpdateAsync(entity);
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