using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Models;
using Sufni.App.SessionGraph;
using Sufni.App.Stores;

using static Sufni.App.Services.PersistenceGuards;

namespace Sufni.App.Services;

public interface ISessionRepository
{
    Task<List<Session>> GetSessionsAsync();

    Task<Session?> GetSessionAsync(Guid id);

    Task<List<Guid>> GetIncompleteSessionIdsAsync();

    /// <summary>
    /// Sessions missing a processed BLOB (data IS NULL), each paired with its
    /// stored processing fingerprint. The sync session-data pull uses the
    /// fingerprint as the download match target so a fill commits only when the
    /// downloaded bytes match what the row's metadata already advertises.
    /// </summary>
    Task<List<(Guid Id, string? Fingerprint)>> GetIncompleteSessionIdsWithFingerprintAsync();

    Task<byte[]?> GetSessionRawPsstAsync(Guid id);

    /// <summary>
    /// The processed BLOB together with the fingerprint of those bytes, or null
    /// when the row holds no data. The sync session-data push sends both so the
    /// hub can reject a fingerprint mismatch (download-then-swap).
    /// </summary>
    Task<(byte[] Data, string? Fingerprint)?> GetSessionRawPsstWithFingerprintAsync(Guid id);

    Task<List<TrackPoint>?> GetSessionTrackAsync(Guid id);

    Task<Guid> PutSessionAsync(Session session);

    Task<Session> PutProcessedSessionAsync(Session session, Track? newFullTrack, RecordedSessionSource? source);

    /// <summary>
    /// Derived-only processed write used by the recompute engine. Writes only the
    /// derived columns (data, fingerprint, summary metrics, cached track,
    /// full_track_id), optionally inserting a new full track, and never touches
    /// user-authored metadata. Inside one transaction it re-reads the row and
    /// recomputes the DB-resident part of the fingerprint; if it no longer matches
    /// <paramref name="expectedInputFingerprint"/> (a passive setup/bike/source
    /// change raced the run) it rolls back and returns null. The preference-stored
    /// processing option is not re-checked here — the engine guards it.
    /// </summary>
    Task<Session?> UpdateProcessedDerivedDataAsync(
        Session session,
        Track? newFullTrack,
        ProcessingFingerprint expectedInputFingerprint);

    Task UpdateSessionPsstAsync(Guid id, byte[] data, string? fingerprintJson, SessionSummaryMetrics metrics);

    Task UpdateSessionTrackAsync(
        Guid id,
        List<TrackPoint> points,
        SessionSummaryMetrics metrics,
        double? gpsOffsetSeconds = null);
}

internal sealed class SessionRepository(
    SqliteConnectionContext connectionContext,
    IProcessingFingerprintService fingerprintService) : ISessionRepository
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
                                                                      gps_offset_seconds,
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
                                                             gps_offset_seconds=?,
                                                             session_processing_fingerprint=?,
                                                             track=?,
                                                             data=?,
                                                             front_springrate=?, front_hsc=?, front_lsc=?, front_lsr=?, front_hsr=?,
                                                             rear_springrate=?, rear_hsc=?, rear_lsc=?, rear_lsr=?, rear_hsr=?,
                                                             updated=?,
                                                             deleted=NULL
                                                             """;

    // Metadata save writes user-authored columns only. full_track_id,
    // gps_offset_seconds, and session_processing_fingerprint are derived columns
    // owned by the processed-write path; they are deliberately NOT listed here so
    // a metadata save preserves whatever the derived path last wrote.
    private const string SessionMetadataSaveUpdateAssignments = """
                                                                name=?,
                                                                setup_id=?,
                                                                description=?,
                                                                timestamp=?,
                                                                track=COALESCE(?, track),
                                                                data=COALESCE(?, data),
                                                                front_springrate=?, front_hsc=?, front_lsc=?, front_lsr=?, front_hsr=?,
                                                                rear_springrate=?, rear_hsc=?, rear_lsc=?, rear_lsr=?, rear_hsr=?,
                                                                updated=?,
                                                                deleted=NULL
                                                                """;

    // Derived-only assignment: the columns owned by the processed-write pipeline,
    // i.e. ProcessedSessionUpdateAssignments minus user-authored metadata (name,
    // setup_id, description, timestamp, tuning) and minus gps_offset_seconds (the
    // GPS-offset command owns that). It never resurrects a soft-deleted row, so it
    // omits deleted=NULL and the caller filters WHERE deleted IS NULL.
    private const string DerivedSessionUpdateAssignments = """
                                                           duration_seconds=?,
                                                           distance_meters=?,
                                                           ascent_meters=?,
                                                           descent_meters=?,
                                                           full_track_id=?,
                                                           session_processing_fingerprint=?,
                                                           track=?,
                                                           data=?,
                                                           updated=?
                                                           """;

    private static readonly string UpdateProcessedSessionSql = $"""
                                                                UPDATE session
                                                                SET
                                                                    {ProcessedSessionUpdateAssignments}
                                                                WHERE
                                                                    id=?
                                                                """;

    private static readonly string UpdateProcessedDerivedDataSql = $"""
                                                                    UPDATE session
                                                                    SET
                                                                        {DerivedSessionUpdateAssignments}
                                                                    WHERE
                                                                        id=? AND deleted IS NULL
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

    public async Task<List<(Guid Id, string? Fingerprint)>> GetIncompleteSessionIdsWithFingerprintAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        const string query = $"""
                              SELECT id, {SessionSqlProjection.ProcessingFingerprintColumn}
                              FROM session
                              WHERE deleted IS null AND data IS null
                              """;
        var sessions = await connection.QueryAsync<Session>(query);
        return sessions.Select(session => (session.Id, session.ProcessingFingerprintJson)).ToList();
    }

    public async Task<byte[]?> GetSessionRawPsstAsync(Guid id)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var sessions = await connection.QueryAsync<Session>(
            "SELECT data FROM session WHERE deleted IS null AND id = ?", id);
        return sessions.Count == 1 ? sessions[0].ProcessedData : null;
    }

    public async Task<(byte[] Data, string? Fingerprint)?> GetSessionRawPsstWithFingerprintAsync(Guid id)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var sessions = await connection.QueryAsync<Session>(
            $"""
             SELECT data, {SessionSqlProjection.ProcessingFingerprintColumn}
             FROM session
             WHERE deleted IS null AND id = ?
             """,
            id);
        return sessions.Count == 1 && sessions[0].ProcessedData is { } data
            ? (data, sessions[0].ProcessingFingerprintJson)
            : null;
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
        return await PutProcessedSessionCoreAsync(session, newFullTrack, source)
               ?? throw new InvalidOperationException($"Session {session.Id} was not found after processed-session persistence.");
    }

    public async Task<Session?> UpdateProcessedDerivedDataAsync(
        Session session,
        Track? newFullTrack,
        ProcessingFingerprint expectedInputFingerprint)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        await connection.ExecuteAsync("BEGIN TRANSACTION");

        try
        {
            // Coherence guard: re-check the DB-resident inputs that produced
            // the reprocess against the freshly read row, inside the write
            // transaction. A mismatch means a passive setup/bike/source change
            // raced this run, so roll back and return null (the engine re-enqueues).
            // The preference-stored processing option is not a DB column and is
            // guarded by the engine's commit-time still-current check instead.
            if (!await CurrentDatabaseInputsMatchAsync(connection, session.Id, expectedInputFingerprint))
            {
                await connection.ExecuteAsync("ROLLBACK");
                return null;
            }

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

            session.Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var updatedRows = await connection.ExecuteAsync(
                UpdateProcessedDerivedDataSql,
                CreateDerivedDataUpdateValuesWithId(session));
            if (updatedRows == 0)
            {
                await connection.ExecuteAsync("ROLLBACK");
                return null;
            }

            await connection.ExecuteAsync("COMMIT");
        }
        catch
        {
            await connection.ExecuteAsync("ROLLBACK");
            throw;
        }

        return await GetSessionAsync(session.Id)
               ?? throw new InvalidOperationException($"Session {session.Id} was not found after derived-data persistence.");
    }

    private async Task<bool> CurrentDatabaseInputsMatchAsync(
        SQLiteAsyncConnection connection,
        Guid sessionId,
        ProcessingFingerprint expectedInputFingerprint)
    {
        // Re-read via the metadata projection (no BLOB) to get the row's current
        // setup linkage, then resolve setup/bike/source as they stand now.
        var session = await GetSessionAsync(sessionId);
        if (session?.Setup is not { } setupId)
        {
            return false;
        }

        var setup = await connection.FindAsync<Setup>(setupId);
        if (setup is null || setup.Deleted is not null)
        {
            return false;
        }

        var bike = await connection.FindAsync<Bike>(setup.BikeId);
        if (bike is null || bike.Deleted is not null)
        {
            return false;
        }

        var source = await connection.FindAsync<RecordedSessionSource>(sessionId);
        if (source is null)
        {
            return false;
        }

        var current = fingerprintService.CreateCurrentDatabaseInputs(
            SessionSnapshot.From(session),
            SetupSnapshot.From(setup, boardId: null),
            BikeSnapshot.From(bike),
            RecordedSessionSourceSnapshot.From(source));
        return expectedInputFingerprint.MatchesDatabaseInputs(current);
    }

    public async Task UpdateSessionPsstAsync(Guid id, byte[] data, string? fingerprintJson, SessionSummaryMetrics metrics)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        // Writes the BLOB-bound pair (data + its fingerprint) and the metrics
        // recomputed from the new bytes, with NO `updated` bump — a processed-BLOB
        // swap/fill must not create a metadata-sync feedback edge.
        var updatedRows = await connection.ExecuteAsync(
            $"""
            UPDATE session
            SET
                data=?,
                {SessionSqlProjection.ProcessingFingerprintColumn}=?,
                duration_seconds=?,
                distance_meters=?,
                ascent_meters=?,
                descent_meters=?
            WHERE id=? AND deleted IS NULL
            """,
            data,
            fingerprintJson,
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

    public async Task UpdateSessionTrackAsync(
        Guid id,
        List<TrackPoint> points,
        SessionSummaryMetrics metrics,
        double? gpsOffsetSeconds = null)
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
                gps_offset_seconds=COALESCE(?, gps_offset_seconds),
                updated=?
            WHERE id=? AND deleted IS NULL
            """,
            pointsJson,
            metrics.DurationSeconds,
            metrics.DistanceMeters,
            metrics.AscentMeters,
            metrics.DescentMeters,
            gpsOffsetSeconds,
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
        RecordedSessionSource? source)
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
                var updatedRows = await UpdateProcessedSessionAsync(connection, session);
                if (updatedRows == 0)
                {
                    await connection.ExecuteAsync("ROLLBACK");
                    return null;
                }
            }
            else
            {
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
        NormalizeGpsOffsetSeconds(session.GpsOffsetSeconds),
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

    private static object?[] CreateProcessedSessionUpdateValuesWithId(Session session) =>
    [
        .. CreateProcessedSessionUpdateValues(session),
        session.Id
    ];

    // Values for DerivedSessionUpdateAssignments (derived columns only), in column
    // order, followed by the id used in the WHERE clause.
    private static object?[] CreateDerivedDataUpdateValuesWithId(Session session) =>
    [
        session.DurationSeconds,
        session.DistanceMeters,
        session.AscentMeters,
        session.DescentMeters,
        session.FullTrack,
        session.ProcessingFingerprintJson,
        SerializeTrack(session),
        session.ProcessedData,
        session.Updated,
        session.Id
    ];

    private static object?[] CreateSessionMetadataSaveUpdateValuesWithId(Session session) =>
    [
        session.Name,
        session.Setup,
        session.Description,
        session.Timestamp,
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

    private static double NormalizeGpsOffsetSeconds(double gpsOffsetSeconds) =>
        double.IsFinite(gpsOffsetSeconds) ? gpsOffsetSeconds : 0;

}
