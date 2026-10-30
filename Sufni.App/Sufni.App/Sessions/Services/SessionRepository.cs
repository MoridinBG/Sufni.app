using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SQLite;

using static Sufni.App.Infrastructure.PersistenceGuards;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Bikes.Models;
using Sufni.App.Sessions.Store;
namespace Sufni.App.Sessions.Services;

public interface ISessionRepository
{
    Task<List<Session>> GetSessionsAsync();

    Task<Session?> GetSessionAsync(Guid id);

    Task<List<Guid>> GetActiveSessionIdsAsync();

    Task<bool> HasOtherActiveSessionWithFullTrackAsync(Guid fullTrackId, Guid excludingSessionId);

    Task<SessionProcessingInputBundle?> GetProcessingInputBundleAsync(Guid sessionId);

    Task<List<Guid>> GetIncompleteSessionIdsAsync();

    /// <summary>
    /// Sessions missing a processed BLOB (data IS NULL), each paired with its
    /// stored processing fingerprint. The sync session-data pull uses the
    /// fingerprint as the download match target so a fill commits only when the
    /// downloaded bytes match what the row's metadata already advertises.
    /// </summary>
    Task<List<(Guid Id, string? Fingerprint)>> GetIncompleteSessionIdsWithFingerprintAsync();

    Task<byte[]?> GetSessionRawPsstAsync(Guid id);

    Task<byte[]?> GetSessionRawPsstAsync(Guid id, long processedTelemetryRevision);

    Task<SessionPsstPayloadMetadata?> GetSessionPsstPayloadMetadataAsync(Guid id);

    /// <summary>
    /// The processed BLOB together with the fingerprint of those bytes, or null
    /// when the row holds no data. The sync session-data push sends both so the
    /// hub can reject a fingerprint mismatch (download-then-swap).
    /// </summary>
    Task<(byte[] Data, string? Fingerprint)?> GetSessionRawPsstWithFingerprintAsync(Guid id);

    Task<List<TrackPoint>?> GetSessionTrackAsync(Guid id);

    Task<List<TrackPoint>?> GetSessionTrackAsync(Guid id, long trackProjectionRevision);

    Task<Guid> PutSessionAsync(Session session);

    Task<Session?> SetProcessingOptionAsync(Guid id, int windowMilliseconds);
    Task<Session?> RestoreProcessingOptionAsync(Guid id, int expectedWindowMilliseconds, int? previousWindowMilliseconds);

    Task<Session> PutProcessedSessionAsync(Session session, Track? newFullTrack, RecordedSessionSource? source);

    /// <summary>
    /// Derived-only processed write used by the recompute engine. Writes only the
    /// derived columns (data, fingerprint, summary metrics, cached track,
    /// full_track_id), optionally inserting a new full track, and never touches
    /// user-authored metadata. In the transaction it compares current database
    /// inputs with <paramref name="expectedInputs"/> captured and validated by
    /// the caller. A changed or missing input rolls back the write and returns null.
    /// </summary>
    Task<Session?> UpdateProcessedDerivedDataAsync(
        Session session,
        Track? newFullTrack,
        SessionProcessingInputBundle expectedInputs);

    Task UpdateSessionPsstAsync(Guid id, byte[] data, string? fingerprintJson, SessionSummaryMetrics metrics);

    Task UpdateSessionProcessedGenerationAsync(
        Guid id,
        byte[] data,
        string? fingerprintJson,
        SessionProcessedGeneration generation);

    Task UpdateSessionTrackAsync(
        Guid id,
        List<TrackPoint> points,
        SessionSummaryMetrics metrics,
        double? gpsOffsetSeconds = null);
}

public sealed record SessionPsstPayloadMetadata(
    Guid Id,
    bool HasData,
    long ProcessedTelemetryRevision,
    string? ProcessingFingerprintJson);

internal sealed class SessionRepository(
    SqliteConnectionContext connectionContext,
    TimeProvider timeProvider) : ISessionRepository
{
    private sealed class RollbackWithoutResultException : Exception
    {
    }

    private static readonly string ActiveSessionMetadataProjection = $"""
                                                                     id,
                                                                     name,
                                                                     setup_id,
                                                                     description,
                                                                     timestamp,
                                                                     velocity_filter_window_ms,
                                                                     duration_seconds,
                                                                     distance_meters,
                                                                      ascent_meters,
                                                                      descent_meters,
                                                                      full_track_id,
                                                                      gps_offset_seconds,
                                                                      source_session_id,
                                                                      window_start_seconds,
                                                                      window_end_seconds,
                                                                      previous_window_start_seconds,
                                                                      previous_window_end_seconds,
                                                                      processed_telemetry_revision,
                                                                      track_projection_revision,
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
                                                             velocity_filter_window_ms=?,
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

    private const string SessionMetadataSaveUpdateAssignments = """
                                                                name=?,
                                                                setup_id=?,
                                                                description=?,
                                                                velocity_filter_window_ms=?,
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
                                                           data=?
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

    private const string ProcessingInputBundleSql = """
                                                    SELECT
                                                        s.id AS session_id,
                                                        s.setup_id AS setup_id,
                                                        s.velocity_filter_window_ms AS velocity_filter_window_ms,
                                                        s.source_session_id AS window_source_session_id,
                                                        s.window_start_seconds AS window_start_seconds,
                                                        s.window_end_seconds AS window_end_seconds,
                                                        setup.bike_id AS bike_id,
                                                        setup.front_sensor_configuration AS front_sensor_configuration,
                                                        setup.rear_sensor_configuration AS rear_sensor_configuration,
                                                        bike.head_angle AS head_angle,
                                                        bike.fork_stroke AS fork_stroke,
                                                        bike.shock_stroke AS shock_stroke,
                                                        bike.rear_suspension AS rear_suspension,
                                                        source.session_id AS source_session_id,
                                                        source.source_kind AS source_kind,
                                                        source.source_name AS source_name,
                                                        source.schema_version AS schema_version,
                                                        source.source_hash AS source_hash
                                                    FROM session s
                                                    JOIN setup ON setup.id = s.setup_id AND setup.deleted IS NULL
                                                    JOIN bike ON bike.id = setup.bike_id AND bike.deleted IS NULL
                                                    JOIN session_recording_source source ON source.session_id = COALESCE(s.source_session_id, s.id)
                                                    WHERE s.deleted IS NULL AND s.id = ?
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
        return [.. await connection.QueryAsync<SessionMetadataRow>(query)];
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
        var sessions = await connection.QueryAsync<SessionMetadataRow>(query, id);
        return sessions.Count == 1 ? sessions[0] : null;
    }

    public async Task<List<Guid>> GetActiveSessionIdsAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        var rows = await connection.QueryAsync<SessionIdRow>(
            """
            SELECT id
            FROM session
            WHERE deleted IS NULL
            ORDER BY timestamp DESC
            """);
        return rows.Select(row => row.Id).ToList();
    }

    public async Task<bool> HasOtherActiveSessionWithFullTrackAsync(Guid fullTrackId, Guid excludingSessionId)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        var exists = await connection.ExecuteScalarAsync<int>(
            """
            SELECT EXISTS(
                SELECT 1
                FROM session
                WHERE deleted IS NULL
                  AND id <> ?
                  AND full_track_id = ?
                LIMIT 1
            )
            """,
            excludingSessionId,
            fullTrackId);
        return exists != 0;
    }

    public async Task<SessionProcessingInputBundle?> GetProcessingInputBundleAsync(Guid sessionId)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var rows = await connection.QueryAsync<ProcessingInputBundleRow>(ProcessingInputBundleSql, sessionId);
        return rows.Count == 1 ? rows[0].ToBundle() : null;
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

    public async Task<byte[]?> GetSessionRawPsstAsync(Guid id, long processedTelemetryRevision)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var sessions = await connection.QueryAsync<Session>(
            "SELECT data FROM session WHERE deleted IS null AND id = ? AND processed_telemetry_revision = ?",
            id,
            processedTelemetryRevision);
        return sessions.Count == 1 ? sessions[0].ProcessedData : null;
    }

    public async Task<SessionPsstPayloadMetadata?> GetSessionPsstPayloadMetadataAsync(Guid id)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var rows = await connection.QueryAsync<SessionPsstPayloadMetadataRow>(
            $"""
             SELECT
                id,
                {SessionSqlProjection.HasDataProjection},
                processed_telemetry_revision,
                {SessionSqlProjection.ProcessingFingerprintColumn}
             FROM session
             WHERE deleted IS null AND id = ?
             """,
            id);
        return rows.Count == 1 ? rows[0].ToMetadata() : null;
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

    public async Task<List<TrackPoint>?> GetSessionTrackAsync(Guid id, long trackProjectionRevision)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var sessions = await connection.QueryAsync<Session>(
            "SELECT track FROM session WHERE deleted IS null AND id = ? AND track_projection_revision = ?",
            id,
            trackProjectionRevision);
        return sessions.Count == 1 ? sessions[0].Track : null;
    }

    public async Task<Session?> SetProcessingOptionAsync(Guid id, int windowMilliseconds)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        await connection.ExecuteAsync(
            "UPDATE session SET velocity_filter_window_ms = ?, updated = ? WHERE id = ? AND deleted IS NULL",
            windowMilliseconds, timeProvider.GetUtcNow().ToUnixTimeSeconds(), id);
        return await GetSessionAsync(id);
    }

    public async Task<Session?> RestoreProcessingOptionAsync(Guid id, int expectedWindowMilliseconds, int? previousWindowMilliseconds)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        await connection.ExecuteAsync(
            "UPDATE session SET velocity_filter_window_ms = ?, updated = ? WHERE id = ? AND deleted IS NULL AND velocity_filter_window_ms = ?",
            previousWindowMilliseconds, timeProvider.GetUtcNow().ToUnixTimeSeconds(), id, expectedWindowMilliseconds);
        return await GetSessionAsync(id);
    }

    public async Task<Guid> PutSessionAsync(Session session)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        var existing = await EntityExistsAsync<Session>(connection, session.Id);
        session.Updated = timeProvider.GetUtcNow().ToUnixTimeSeconds();
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
        SessionProcessingInputBundle expectedInputs)
    {
        try
        {
            await connectionContext.RunInTransactionAsync(connection =>
            {
                // The owner validated this input snapshot before entering the transaction.
                // Reject a dependency edit that landed after that validation.
                if (!CurrentDatabaseInputsMatch(connection, session.Id, expectedInputs))
                {
                    throw new RollbackWithoutResultException();
                }

                if (newFullTrack is not null)
                {
                    var existingTrack = EntityExists<Track>(connection, newFullTrack.Id);
                    newFullTrack.Updated = timeProvider.GetUtcNow().ToUnixTimeSeconds();
                    newFullTrack.Deleted = null;

                    if (existingTrack)
                    {
                        UpdateEntity(connection, newFullTrack);
                    }
                    else
                    {
                        InsertEntity(connection, newFullTrack);
                    }

                    session.FullTrack = newFullTrack.Id;
                }

                var updatedRows = connection.Execute(
                    UpdateProcessedDerivedDataSql,
                    CreateDerivedDataUpdateValuesWithId(session));
                if (updatedRows == 0)
                {
                    throw new RollbackWithoutResultException();
                }
            });
        }
        catch (RollbackWithoutResultException)
        {
            return null;
        }

        return await GetSessionAsync(session.Id)
               ?? throw new InvalidOperationException($"Session {session.Id} was not found after derived-data persistence.");
    }

    private static bool CurrentDatabaseInputsMatch(
        SQLiteConnection connection,
        Guid sessionId,
        SessionProcessingInputBundle expectedInputs)
    {
        var input = GetProcessingInputBundle(connection, sessionId);
        return input is not null && input == expectedInputs;
    }

    private static SessionProcessingInputBundle? GetProcessingInputBundle(SQLiteConnection connection, Guid sessionId)
    {
        var rows = connection.Query<ProcessingInputBundleRow>(ProcessingInputBundleSql, sessionId);
        return rows.Count == 1 ? rows[0].ToBundle() : null;
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

    public async Task UpdateSessionProcessedGenerationAsync(
        Guid id,
        byte[] data,
        string? fingerprintJson,
        SessionProcessedGeneration generation)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var trackJson = generation.Track is null ? null : AppJson.Serialize(generation.Track);
        var updatedRows = await connection.ExecuteAsync(
            $"""
            UPDATE session
            SET
                data=?,
                {SessionSqlProjection.ProcessingFingerprintColumn}=?,
                duration_seconds=?,
                distance_meters=?,
                ascent_meters=?,
                descent_meters=?,
                full_track_id=?,
                track=?
            WHERE id=? AND deleted IS NULL
            """,
            data,
            fingerprintJson,
            generation.DurationSeconds,
            generation.DistanceMeters,
            generation.AscentMeters,
            generation.DescentMeters,
            generation.FullTrackId,
            trackJson,
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
        var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
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
        if (source is not null && source.SessionId != session.Id)
        {
            throw new InvalidOperationException("Recorded session source must belong to the processed session.");
        }

        try
        {
            await connectionContext.RunInTransactionAsync(connection =>
            {
                if (newFullTrack is not null)
                {
                    var existingTrack = EntityExists<Track>(connection, newFullTrack.Id);
                    newFullTrack.Updated = timeProvider.GetUtcNow().ToUnixTimeSeconds();
                    newFullTrack.Deleted = null;

                    if (existingTrack)
                    {
                        UpdateEntity(connection, newFullTrack);
                    }
                    else
                    {
                        InsertEntity(connection, newFullTrack);
                    }

                    session.FullTrack = newFullTrack.Id;
                }

                var existingSession = EntityExists<Session>(connection, session.Id);
                session.Updated = timeProvider.GetUtcNow().ToUnixTimeSeconds();
                session.Deleted = null;

                if (existingSession)
                {
                    var updatedRows = UpdateProcessedSession(connection, session);
                    if (updatedRows == 0)
                    {
                        throw new RollbackWithoutResultException();
                    }
                }
                else
                {
                    InsertEntity(connection, session);
                }

                if (source is not null)
                {
                    RecordedSessionSourceRepository.PutRecordedSessionSourceInTransaction(
                        connection,
                        source);
                }
            });
        }
        catch (RollbackWithoutResultException)
        {
            return null;
        }

        return await GetSessionAsync(session.Id)
               ?? throw new InvalidOperationException($"Session {session.Id} was not found after processed-session persistence.");
    }

    private static int UpdateProcessedSession(
        SQLiteConnection connection,
        Session session)
    {
        return connection.Execute(
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
        session.VelocityFilterWindowMilliseconds,
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
        session.Id
    ];

    private static object?[] CreateSessionMetadataSaveUpdateValuesWithId(Session session) =>
    [
        session.Name,
        session.Setup,
        session.Description,
        session.VelocityFilterWindowMilliseconds,
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

    private sealed class SessionIdRow
    {
        [Column("id")]
        public Guid Id { get; set; }
    }

    private sealed class SessionPsstPayloadMetadataRow
    {
        [Column("id")]
        public Guid Id { get; set; }

        [Column("processed_data_available")]
        public bool HasData { get; set; }

        [Column("processed_telemetry_revision")]
        public long ProcessedTelemetryRevision { get; set; }

        [Column("session_processing_fingerprint")]
        public string? ProcessingFingerprintJson { get; set; }

        public SessionPsstPayloadMetadata ToMetadata() => new(
            Id,
            HasData,
            ProcessedTelemetryRevision,
            ProcessingFingerprintJson);
    }

    private sealed class ProcessingInputBundleRow
    {
        [Column("session_id")]
        public Guid SessionId { get; set; }

        [Column("velocity_filter_window_ms")]
        public int? VelocityFilterWindowMilliseconds { get; set; }

        [Column("setup_id")]
        public Guid SetupId { get; set; }

        [Column("window_source_session_id")]
        public Guid? WindowSourceSessionId { get; set; }

        [Column("window_start_seconds")]
        public double WindowStartSeconds { get; set; }

        [Column("window_end_seconds")]
        public double? WindowEndSeconds { get; set; }

        [Column("bike_id")]
        public Guid BikeId { get; set; }

        [Column("front_sensor_configuration")]
        public string? FrontSensorConfigurationJson { get; set; }

        [Column("rear_sensor_configuration")]
        public string? RearSensorConfigurationJson { get; set; }

        [Column("head_angle")]
        public double HeadAngle { get; set; }

        [Column("fork_stroke")]
        public double? ForkStroke { get; set; }

        [Column("shock_stroke")]
        public double? ShockStroke { get; set; }

        [Column("rear_suspension")]
        public string RearSuspensionJson { get; set; } = null!;

        [Column("source_session_id")]
        public Guid SourceSessionId { get; set; }

        [Column("source_kind")]
        public string SourceKindValue { get; set; } = null!;

        [Column("source_name")]
        public string SourceName { get; set; } = null!;

        [Column("schema_version")]
        public int SchemaVersion { get; set; }

        [Column("source_hash")]
        public string SourceHash { get; set; } = null!;

        public SessionProcessingInputBundle? ToBundle()
        {
            if (!RearSuspensionJsonCodec.TryDeserialize(RearSuspensionJson, out var rearSuspension) ||
                rearSuspension is null ||
                string.IsNullOrWhiteSpace(SourceKindValue) ||
                string.IsNullOrWhiteSpace(SourceName) ||
                string.IsNullOrWhiteSpace(SourceHash))
            {
                return null;
            }

            RecordedSessionSourceKind sourceKind;
            try
            {
                sourceKind = RecordedSessionSourceKindExtensions.FromStorageValue(SourceKindValue);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }

            return new SessionProcessingInputBundle(
                new SessionProcessingInput(
                    SessionId,
                    SetupId,
                    WindowSourceSessionId is { } sourceSessionId
                        ? new RecordedSessionDerivationWindow(sourceSessionId, WindowStartSeconds, WindowEndSeconds)
                        : null,
                    VelocityFilterWindowMilliseconds),
                new SetupProcessingInput(
                    SetupId,
                    BikeId,
                    FrontSensorConfigurationJson,
                    RearSensorConfigurationJson),
                new BikeProcessingInput(
                    BikeId,
                    HeadAngle,
                    ForkStroke,
                    ShockStroke,
                    RearSuspensionJson!),
                new RecordedSessionSourceSnapshot(
                    SourceSessionId,
                    sourceKind,
                    SourceName,
                    SchemaVersion,
                    SourceHash));
        }
    }
}
