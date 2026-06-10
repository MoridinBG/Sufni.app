using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
}

internal sealed class SessionRepository(
    SqliteConnectionContext connectionContext,
    ISessionTelemetryProcessor sessionTelemetryProcessor) : ISessionRepository
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
}
