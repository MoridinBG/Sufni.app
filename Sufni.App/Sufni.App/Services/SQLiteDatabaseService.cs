using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MessagePack;
using SQLite;
using Sufni.App.Models;
using Sufni.App.Models.Telemetry;

namespace Sufni.App.Services;

public class SqLiteDatabaseService : IDatabaseService
{
    private Task Initialization { get; }
    private readonly SQLiteAsyncConnection connection;

    private static readonly List<CalibrationMethod> DefaultCalibrationMethods =
    [
        new(CalibrationMethod.FractionId,
            "fraction",
            "Sample is in fraction of maximum suspension stroke.",
            new CalibrationMethodProperties([], [], "sample * MAX_STROKE")),
        new(CalibrationMethod.PercentageId,
            "percentage",
            "Sample is in percentage of maximum suspension stroke.",
            new CalibrationMethodProperties(
                [],
                new Dictionary<string, string>()
                {
                    {"factor", "MAX_STROKE / 100.0"}
                },
                "sample * factor")),
        new(CalibrationMethod.LinearId,
            "linear",
            "Sample is linearly distributed within a given range.",
            new CalibrationMethodProperties(
                [
                    "min_measurement",
                    "max_measurement"
                ],
                new Dictionary<string, string>()
                {
                    {"factor", "MAX_STROKE / (max_measurement - min_measurement)"}
                },
                "(sample - min_measurement) * factor")),
        new(CalibrationMethod.LinearPotmeterId,
            "linear-potmeter",
            "Sample is the ADC value read from a linear potentiometer.",
            new CalibrationMethodProperties(
                [
                    "stroke",
                    "resolution"
                ],
                new Dictionary<string, string>()
                {
                    {"factor", "stroke / (2^resolution)"}
                },
                "sample * factor")),
        new(CalibrationMethod.IsoscelesId,
            "as5600-isosceles-triangle",
            "Triangle setup with the sensor between the base and leg.",
            new CalibrationMethodProperties(
                [
                    "arm",
                    "max"
                ],
                new Dictionary<string, string>()
                {
                    {"start_angle", "acos(max / 2.0 / arm)"},
                    {"factor", "2.0 * pi / 4096"},
                    {"dbl_arm", "2.0 * arm"},
                },
                "max - (dbl_arm * cos((factor*sample) + start_angle))")),
        new(CalibrationMethod.TriangleId,
            "as5600-triangle",
            "Triangle setup with the sensor between two known sides.",
            new CalibrationMethodProperties(
                [
                    "arm1",
                    "arm2",
                    "max"
                ],
                new Dictionary<string, string>()
                {
                    {"start_angle", "acos((arm1^2+arm2^2-max^2)/(2*arm1*arm2))"},
                    {"factor", "2.0 * pi / 4096"},
                    {"arms_sqr_sum", "arm1^2 + arm2^2"},
                    {"dbl_arm1_arm2", "2 * arm1 * arm2"},
                },
                "max - sqrt(arms_sqr_sum - dbl_arm1_arm2 * cos(start_angle-(factor*sample)))")),
    ];

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
        var result = await connection.CreateTablesAsync(CreateFlags.None, new[]
        {
            typeof(Board),
            typeof(Linkage),
            typeof(CalibrationMethod),
            typeof(Calibration),
            typeof(Setup),
            typeof(Session),
            typeof(SessionCache),
            typeof(Synchronization)
        });

        if (result.Results[typeof(CalibrationMethod)] == CreateTableResult.Created)
        {
            var timestamp = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
            foreach (var calibrationMethod in DefaultCalibrationMethods)
            {
                calibrationMethod.Updated = timestamp;
            }
            await connection.InsertAllAsync(DefaultCalibrationMethods);
        }
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

    public async Task DeleteBoardAsync(string id)
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

    public async Task<List<Linkage>> GetLinkagesAsync()
    {
        await Initialization;

        return await connection.Table<Linkage>().Where(t => t.Deleted == null).ToListAsync();
    }

    public async Task<List<Linkage>> GetChangedLinkagesAsync(int since)
    {
        await Initialization;

        return await connection.Table<Linkage>()
            .Where(l => l.Updated > since || (l.Deleted != null && l.Deleted > since))
            .ToListAsync();
    }

    public async Task<Linkage?> GetLinkageAsync(Guid id)
    {
        await Initialization;

        return await connection.Table<Linkage>()
            .Where(l => l.Id == id && l.Deleted == null)
            .FirstOrDefaultAsync();
    }

    public async Task<Guid> PutLinkageAsync(Linkage linkage)
    {
        await Initialization;

        var existing = await connection.Table<Linkage>()
            .Where(l => l.Id == linkage.Id && l.Deleted == null)
            .FirstOrDefaultAsync() is not null;
        linkage.Updated = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
        if (existing)
        {
            await connection.UpdateAsync(linkage);
        }
        else
        {
            await connection.InsertAsync(linkage);
        }

        return linkage.Id;
    }

    public async Task DeleteLinkageAsync(Guid id)
    {
        await Initialization;
        var linkage = await connection.Table<Linkage>()
            .Where(l => l.Id == id)
            .FirstOrDefaultAsync();
        if (linkage is not null)
        {
            linkage.Deleted = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
            await connection.UpdateAsync(linkage);
        }
    }

    public async Task<List<CalibrationMethod>> GetCalibrationMethodsAsync()
    {
        await Initialization;
        return await connection.Table<CalibrationMethod>().Where(c => c.Deleted == null).ToListAsync();
    }

    public async Task<List<CalibrationMethod>> GetChangedCalibrationMethodsAsync(int since)
    {
        await Initialization;

        return await connection.Table<CalibrationMethod>()
            .Where(c => c.Updated > since || (c.Deleted != null && c.Deleted > since))
            .ToListAsync();
    }

    public async Task<CalibrationMethod?> GetCalibrationMethodAsync(Guid id)
    {
        await Initialization;

        return await connection.Table<CalibrationMethod>()
            .Where(c => c.Id == id && c.Deleted == null)
            .FirstOrDefaultAsync();
    }

    public async Task<Guid> PutCalibrationMethodAsync(CalibrationMethod calibrationMethod)
    {
        await Initialization;

        var existing = await connection.Table<CalibrationMethod>()
            .Where(c => c.Id == calibrationMethod.Id && c.Deleted == null)
            .FirstOrDefaultAsync() is not null;
        calibrationMethod.Updated = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
        if (existing)
        {
            await connection.UpdateAsync(calibrationMethod);
        }
        else
        {
            await connection.InsertAsync(calibrationMethod);
        }

        return calibrationMethod.Id;
    }

    public async Task DeleteCalibrationMethodAsync(Guid id)
    {
        await Initialization;
        var calibrationMethod = await connection.Table<CalibrationMethod>()
            .Where(c => c.Id == id)
            .FirstOrDefaultAsync();
        if (calibrationMethod is not null)
        {
            calibrationMethod.Deleted = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
            await connection.UpdateAsync(calibrationMethod);
        }
    }

    public async Task<List<Calibration>> GetCalibrationsAsync()
    {
        await Initialization;
        return await connection.Table<Calibration>().Where(c => c.Deleted == null).ToListAsync();
    }

    public async Task<List<Calibration>> GetChangedCalibrationsAsync(int since)
    {
        await Initialization;

        return await connection.Table<Calibration>()
            .Where(c => c.Updated > since || (c.Deleted != null && c.Deleted > since))
            .ToListAsync();
    }

    public async Task<Calibration?> GetCalibrationAsync(Guid id)
    {
        await Initialization;

        return await connection.Table<Calibration>()
            .Where(c => c.Id == id && c.Deleted == null)
            .FirstOrDefaultAsync();
    }

    public async Task<Guid> PutCalibrationAsync(Calibration calibration)
    {
        await Initialization;

        var existing = await connection.Table<Calibration>()
            .Where(c => c.Id == calibration.Id && c.Deleted == null)
            .FirstOrDefaultAsync() is not null;
        calibration.Updated = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
        if (existing)
        {
            await connection.UpdateAsync(calibration);
        }
        else
        {
            await connection.InsertAsync(calibration);
        }

        return calibration.Id;
    }

    public async Task DeleteCalibrationAsync(Guid id)
    {
        await Initialization;
        var calibration = await connection.Table<Calibration>()
            .Where(c => c.Id == id)
            .FirstOrDefaultAsync();
        if (calibration is not null)
        {
            calibration.Deleted = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
            await connection.UpdateAsync(calibration);
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
        return sessions.Count == 1 ? MessagePackSerializer.Deserialize<TelemetryData>(sessions[0].ProcessedData) : null;
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
}