using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.ExtensionHost.Models;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Services;

public interface ISessionRepository
{
    Task<List<Session>> GetSessionsAsync();

    Task<Session?> GetSessionAsync(Guid id);

    Task<List<Guid>> GetIncompleteSessionIdsAsync();

    Task<TelemetryData?> GetSessionPsstAsync(Guid id);

    Task<byte[]?> GetSessionRawPsstAsync(Guid id);

    Task<List<TrackPoint>?> GetSessionTrackAsync(Guid id);

    Task<Guid> PutSessionAsync(Session session);

    Task<Session> PutProcessedSessionAsync(Session session, Track? newFullTrack, RecordedSessionSource? source);

    Task<Session?> PutProcessedSessionIfUnchangedAsync(
        Session session,
        Track? newFullTrack,
        RecordedSessionSource? source,
        long baselineUpdated);

    Task PatchSessionPsstAsync(Guid id, byte[] data);

    Task PatchSessionTrackAsync(Guid id, List<TrackPoint> points);
}

internal sealed class SessionRepository(
    SqliteConnectionContext connectionContext,
    ISessionTelemetryProcessor sessionTelemetryProcessor,
    ITrackRepository trackRepository) : ISessionRepository
{
    private const string SessionProcessingFingerprintColumn = "session_processing_fingerprint";
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

    public async Task<TelemetryData?> GetSessionPsstAsync(Guid id)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
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

    public async Task PatchSessionPsstAsync(Guid id, byte[] data)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        var session = await connection.Table<Session>()
            .Where(candidate => candidate.Id == id && candidate.Deleted == null)
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
        var connection = await connectionContext.GetInitializedConnectionAsync();

        var session = await connection.Table<Session>()
            .Where(candidate => candidate.Id == id && candidate.Deleted == null)
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
            else if (session.FullTrack is null && session.Timestamp.HasValue)
            {
                session.FullTrack = await trackRepository.FindTrackContainingTimestampAsync(session.Timestamp.Value);
            }

            await ApplySessionSummaryMetricsAsync(connection, session, newFullTrack);

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

    private async Task ApplySessionSummaryMetricsAsync(
        SQLiteAsyncConnection connection,
        Session session,
        Track? generatedFullTrack)
    {
        var durationSeconds = sessionTelemetryProcessor.ReadProcessedDurationSeconds(session.ProcessedData) ?? session.DurationSeconds;
        var points = await GetMetricTrackPointsAsync(connection, session, generatedFullTrack, durationSeconds);
        var metrics = sessionTelemetryProcessor.ComputeSummaryMetrics(durationSeconds, points);

        session.DurationSeconds = metrics.DurationSeconds;
        session.DistanceMeters = metrics.DistanceMeters;
        session.AscentMeters = metrics.AscentMeters;
        session.DescentMeters = metrics.DescentMeters;
    }

    private async Task<IReadOnlyList<TrackPoint>?> GetMetricTrackPointsAsync(
        SQLiteAsyncConnection connection,
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

        return await TryGenerateSessionTrackFromFullTrackAsync(
            connection,
            session.FullTrack,
            session.Timestamp,
            durationSeconds);
    }

    private async Task<List<TrackPoint>?> TryGenerateSessionTrackFromFullTrackAsync(
        SQLiteAsyncConnection connection,
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

    private static string GetTableName<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() where T : new()
    {
        return typeof(T).GetCustomAttribute<TableAttribute>()?.Name
               ?? throw new InvalidOperationException($"Type {typeof(T).Name} is missing a SQLite table attribute.");
    }

    private static async Task<bool> EntityExistsAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteAsyncConnection connection,
        Guid id) where T : Synchronizable, new()
    {
        var tableName = GetTableName<T>();
        return await connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {tableName} WHERE id = ?", id) > 0;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "T is annotated to preserve SQLite-mapped members.")]
    private static Task<int> InsertEntityAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteAsyncConnection connection,
        T entity) where T : new()
    {
        ValidateEntityForPersistence(entity);
        return connection.InsertAsync(entity);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "T is annotated to preserve SQLite-mapped members.")]
    private static Task<int> UpdateEntityAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteAsyncConnection connection,
        T entity) where T : new()
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
}
