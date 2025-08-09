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
            typeof(PairedDevice)
        ]);

        if (result.Results[typeof(Synchronization)] == CreateTableResult.Created)
        {
            await connection.QueryAsync<Synchronization>("INSERT INTO sync VALUES (0)");
        }
    }

    public async Task<List<Board>> GetBoardsAsync()
    {
        await Initialization;

        return await connection.Table<Board>().Where(b => b.Deleted == null).ToListAsync();
    }

    public async Task<List<Board>> GetChangedBoardsAsync(int since)
    {
        await Initialization;

        return await connection.Table<Board>()
            .Where(b => b.Updated > since || (b.Deleted != null && b.Deleted > since))
            .ToListAsync();
    }

    public async Task PutBoardAsync(Board board)
    {
        await Initialization;

        board.Updated = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
        var existing = await connection.Table<Board>()
            .Where(b => b.Id == board.Id && b.Deleted == null)
            .FirstOrDefaultAsync() is not null;
        if (existing)
        {
            await connection.UpdateAsync(board);
        }
        else
        {
            await connection.InsertAsync(board);
        }
    }

    public async Task DeleteBoardAsync(Guid id)
    {
        await Initialization;
        var board = await connection.Table<Board>()
            .Where(b => b.Id == id)
            .FirstOrDefaultAsync();
        if (board is not null)
        {
            board.Deleted = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
            await connection.UpdateAsync(board);
        }
    }

    public async Task<List<Bike>> GetBikesAsync()
    {
        await Initialization;
        return await connection.Table<Bike>().Where(s => s.Deleted == null).ToListAsync();
    }

    public async Task<List<Bike>> GetChangedBikesAsync(int since)
    {
        await Initialization;

        return await connection.Table<Bike>()
            .Where(s => s.Updated > since || (s.Deleted != null && s.Deleted > since))
            .ToListAsync();
    }

    public async Task<Bike?> GetBikeAsync(Guid id)
    {
        await Initialization;

        return await connection.Table<Bike>()
            .Where(s => s.Id == id && s.Deleted == null)
            .FirstOrDefaultAsync();
    }

    public async Task<Guid> PutBikeAsync(Bike bike)
    {
        await Initialization;

        var existing = await connection.Table<Bike>()
            .Where(s => s.Id == bike.Id && s.Deleted == null)
            .FirstOrDefaultAsync() is not null;
        bike.Updated = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
        if (existing)
        {
            await connection.UpdateAsync(bike);
        }
        else
        {
            await connection.InsertAsync(bike);
        }

        return bike.Id;
    }

    public async Task DeleteBikeAsync(Guid id)
    {
        await Initialization;
        var bike = await connection.Table<Bike>()
            .Where(s => s.Id == id)
            .FirstOrDefaultAsync();
        if (bike is not null)
        {
            bike.Deleted = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
            await connection.UpdateAsync(bike);
        }
    }

    public async Task<List<Setup>> GetSetupsAsync()
    {
        await Initialization;
        return await connection.Table<Setup>().Where(s => s.Deleted == null).ToListAsync();
    }

    public async Task<List<Setup>> GetChangedSetupsAsync(int since)
    {
        await Initialization;

        return await connection.Table<Setup>()
            .Where(s => s.Updated > since || (s.Deleted != null && s.Deleted > since))
            .ToListAsync();
    }

    public async Task<Setup?> GetSetupAsync(Guid id)
    {
        await Initialization;

        return await connection.Table<Setup>()
            .Where(s => s.Id == id && s.Deleted == null)
            .FirstOrDefaultAsync();
    }

    public async Task<Guid> PutSetupAsync(Setup setup)
    {
        await Initialization;

        var existing = await connection.Table<Setup>()
            .Where(s => s.Id == setup.Id && s.Deleted == null)
            .FirstOrDefaultAsync() is not null;
        setup.Updated = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
        if (existing)
        {
            await connection.UpdateAsync(setup);
        }
        else
        {
            await connection.InsertAsync(setup);
        }

        return setup.Id;
    }

    public async Task DeleteSetupAsync(Guid id)
    {
        await Initialization;
        var setup = await connection.Table<Setup>()
            .Where(s => s.Id == id)
            .FirstOrDefaultAsync();
        if (setup is not null)
        {
            setup.Deleted = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
            await connection.UpdateAsync(setup);
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

    public async Task<List<Session>> GetChangedSessionsAsync(int since)
    {
        await Initialization;

        return await connection.Table<Session>()
            .Where(s => s.Updated > since || (s.Deleted != null && s.Deleted > since))
            .ToListAsync();
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

    public async Task DeleteSessionAsync(Guid id)
    {
        await Initialization;
        var session = await connection.Table<Session>()
            .Where(s => s.Id == id)
            .FirstOrDefaultAsync();
        if (session is not null)
        {
            session.Deleted = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
            await connection.UpdateAsync(session);
        }
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

    public async Task<int> GetLastSyncTimeAsync()
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
}