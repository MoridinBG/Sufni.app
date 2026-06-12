using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Models;

using static Sufni.App.Services.PersistenceGuards;

namespace Sufni.App.Services;

public interface ISessionRepository
{
    Task<List<Session>> GetSessionsAsync();

    Task<Session?> GetSessionAsync(Guid id);

    Task<List<Guid>> GetIncompleteSessionIdsAsync();

    Task<byte[]?> GetSessionRawPsstAsync(Guid id);

    Task<List<TrackPoint>?> GetSessionTrackAsync(Guid id);

    Task<Guid> PutSessionAsync(Session session);

    Task<Session> PutProcessedSessionAsync(Session session, Track? newFullTrack, RecordedSessionSource? source);

    Task<Session?> PutProcessedSessionIfUnchangedAsync(
        Session session,
        Track? newFullTrack,
        RecordedSessionSource? source,
        long baselineUpdated);

    Task UpdateSessionPsstAsync(Guid id, byte[] data, SessionSummaryMetrics metrics);

    Task UpdateSessionTrackAsync(Guid id, List<TrackPoint> points, SessionSummaryMetrics metrics);
}

internal sealed class SessionRepository(
    SqliteConnectionContext connectionContext) : ISessionRepository
{
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
                                                                     {SessionSqlProjection.ProcessingFingerprintColumn},
                                                                     front_springrate, front_hsc, front_lsc, front_lsr, front_hsr,
                                                                     rear_springrate, rear_hsc, rear_lsc, rear_lsr, rear_hsr,
                                                                     updated,
                                                                     {SessionSqlProjection.HasDataProjection}
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

    public async Task<List<Session>> GetSessionsAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        var query = $"""
                     SELECT
                         {ActiveSessionMetadataProjection}
                     FROM
                         session
                     WHERE
                         deleted IS NULL
                     ORDER BY timestamp DESC
                     """;
        return await connection.QueryAsync<Session>(query);
    }

    public async Task<Session?> GetSessionAsync(Guid id)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

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
        var connection = await connectionContext.GetInitializedConnectionAsync();

        const string query = "SELECT id FROM session WHERE deleted IS null AND data IS null";
        return (await connection.QueryAsync<Session>(query)).Select(session => session.Id).ToList();
    }

    public async Task<byte[]?> GetSessionRawPsstAsync(Guid id)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var sessions = await connection.QueryAsync<Session>(
            "SELECT data FROM session WHERE deleted IS null AND id = ?", id);
        return sessions.Count == 1 ? sessions[0].ProcessedData : null;
    }

    public async Task<List<TrackPoint>?> GetSessionTrackAsync(Guid id)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var sessions = await connection.QueryAsync<Session>(
            "SELECT track FROM session WHERE deleted IS null AND id = ?", id);
        return sessions.Count == 1 ? sessions[0].Track : null;
    }

    public async Task<Guid> PutSessionAsync(Session session)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        var existing = await EntityExistsAsync<Session>(connection, session.Id);
        session.Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        session.Deleted = null;
        if (existing)
        {
            await connection.ExecuteAsync(UpdateSessionMetadataSaveSql, CreateSessionMetadataSaveUpdateValuesWithId(session));
        }
        else
        {
            await InsertEntityAsync(connection, session);
        }

        return session.Id;
    }

    public async Task<Session> PutProcessedSessionAsync(
        Session session,
        Track? newFullTrack,
        RecordedSessionSource? source)
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

    public async Task UpdateSessionPsstAsync(Guid id, byte[] data, SessionSummaryMetrics metrics)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        var updatedRows = await connection.ExecuteAsync(
            """
            UPDATE session
            SET
                data=?,
                duration_seconds=?,
                distance_meters=?,
                ascent_meters=?,
                descent_meters=?
            WHERE id=? AND deleted IS NULL
            """,
            data,
            metrics.DurationSeconds,
            metrics.DistanceMeters,
            metrics.AscentMeters,
            metrics.DescentMeters,
            id);
        if (updatedRows == 0)
        {
            throw new Exception($"Session {id} does not exist.");
        }
    }

    public async Task UpdateSessionTrackAsync(Guid id, List<TrackPoint> points, SessionSummaryMetrics metrics)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        var pointsJson = AppJson.Serialize(points);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var updatedRows = await connection.ExecuteAsync(
            """
            UPDATE session
            SET
                track=?,
                duration_seconds=?,
                distance_meters=?,
                ascent_meters=?,
                descent_meters=?,
                updated=?
            WHERE id=? AND deleted IS NULL
            """,
            pointsJson,
            metrics.DurationSeconds,
            metrics.DistanceMeters,
            metrics.AscentMeters,
            metrics.DescentMeters,
            now,
            id);
        if (updatedRows == 0)
        {
            throw new Exception($"Session {id} does not exist.");
        }
    }

    private async Task<Session?> PutProcessedSessionCoreAsync(
        Session session,
        Track? newFullTrack,
        RecordedSessionSource? source,
        long? baselineUpdated)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        if (source is not null && source.SessionId != session.Id)
        {
            throw new InvalidOperationException("Recorded session source must belong to the processed session.");
        }

        await connection.ExecuteAsync("BEGIN TRANSACTION");

        try
        {
            if (newFullTrack is not null)
            {
                var existingTrack = await EntityExistsAsync<Track>(connection, newFullTrack.Id);
                newFullTrack.Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                newFullTrack.Deleted = null;

                if (existingTrack)
                {
                    await UpdateEntityAsync(connection, newFullTrack);
                }
                else
                {
                    await InsertEntityAsync(connection, newFullTrack);
                }

                session.FullTrack = newFullTrack.Id;
            }

            var existingSession = await EntityExistsAsync<Session>(connection, session.Id);
            session.Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            session.Deleted = null;

            if (existingSession)
            {
                var updatedRows = baselineUpdated.HasValue
                    ? await UpdateProcessedSessionIfUnchangedAsync(connection, session, baselineUpdated.Value)
                    : await UpdateProcessedSessionAsync(connection, session);
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

                await InsertEntityAsync(connection, session);
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

    private static Task<int> UpdateProcessedSessionAsync(
        SQLiteAsyncConnection connection,
        Session session)
    {
        return connection.ExecuteAsync(
            UpdateProcessedSessionSql,
            CreateProcessedSessionUpdateValuesWithId(session));
    }

    private static Task<int> UpdateProcessedSessionIfUnchangedAsync(
        SQLiteAsyncConnection connection,
        Session session,
        long baselineUpdated)
    {
        return connection.ExecuteAsync(
            UpdateProcessedSessionIfUnchangedSql,
            CreateProcessedSessionUpdateValues(session, baselineUpdated));
    }

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

}
