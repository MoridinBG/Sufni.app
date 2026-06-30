using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SQLite;

using static Sufni.App.Infrastructure.PersistenceGuards;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.SessionGraph;
using Sufni.App.Sessions.Services;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Bikes.Models;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Setups.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
namespace Sufni.App.SyncAndPairing.Services;

public interface ISyncDataStore
{
    Task<long> GetLastSyncTimeAsync(string? serverUrl);

    Task UpdateLastSyncTimeAsync(string? serverUrl);

    Task<SynchronizationData> GetSynchronizationDataAsync(long since);

    /// <summary>
    /// Applies a pulled remote delta and returns the processed-BLOB swaps it
    /// discovered: rows that hold a BLOB whose fingerprint differs from the
    /// accepted remote current-schema fingerprint. The session-data phase
    /// consumes them. The list is transient and re-derived if the run does not
    /// complete.
    /// </summary>
    Task<IReadOnlyList<SessionBlobSwap>> ApplyRemoteSynchronizationDataAsync(SynchronizationData data);

    Task MergeAllAsync(SynchronizationData data);
}

internal sealed class SynchronizationMergeEngine(
    SqliteConnectionContext connectionContext,
    ITrackRepository trackRepository,
    IProcessingFingerprintService fingerprintService) : ISyncDataStore
{
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
                                                                       gps_offset_seconds,
                                                                       {SessionSqlProjection.ProcessingFingerprintColumn},
                                                                      track,
                                                                      front_springrate, front_hsc, front_lsc, front_lsr, front_hsr,
                                                                      rear_springrate, rear_hsc, rear_lsc, rear_lsr, rear_hsr,
                                                                      updated,
                                                                      client_updated,
                                                                      deleted,
                                                                      {SessionSqlProjection.HasDataProjection}
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
                                                                   gps_offset_seconds=?,
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

    // The metadata write WITHOUT the BLOB-bound columns: session_processing_fingerprint,
    // data (data is never written by any metadata path), and the BLOB-derived summary
    // metrics (duration/distance/ascent/descent). Used to "defer" that whole set when the
    // row already holds a BLOB, so it keeps honestly advertising the fingerprint AND the
    // metrics of the bytes it holds while the rest of the metadata syncs immediately. The
    // deferred columns then move together when the swap commits the new BLOB (the swap
    // recomputes the metrics from the new bytes), so the row is never left advertising one
    // BLOB's fingerprint with another BLOB's metrics.
    private const string RemoteSessionMetadataExceptFingerprintAssignments = """
                                                                  name=?,
                                                                  setup_id=?,
                                                                  description=?,
                                                                  timestamp=?,
                                                                   full_track_id=?,
                                                                   gps_offset_seconds=?,
                                                                  track=?,
                                                                  front_springrate=?, front_hsc=?, front_lsc=?, front_lsr=?, front_hsr=?,
                                                                  rear_springrate=?, rear_hsc=?, rear_lsc=?, rear_lsr=?, rear_hsr=?,
                                                                  updated=?,
                                                                  client_updated=?,
                                                                  deleted=?
                                                                  """;

    private static readonly string UpdateRemoteSessionMetadataExceptFingerprintSql = $"""
                                                                     UPDATE session
                                                                     SET
                                                                         {RemoteSessionMetadataExceptFingerprintAssignments}
                                                                     WHERE
                                                                         id=?
                                                                     """;

    public async Task<SynchronizationData> GetSynchronizationDataAsync(long since)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        var boards = await GetChangedAsync<Board>(connection, since);
        var bikes = await GetChangedAsync<Bike>(connection, since);
        var setups = await GetChangedAsync<Setup>(connection, since);
        var sessions = await GetChangedAsync<Session>(connection, since);
        var tracks = await GetChangedAsync<Track>(connection, since);

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

    public async Task<IReadOnlyList<SessionBlobSwap>> ApplyRemoteSynchronizationDataAsync(SynchronizationData data)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var swaps = new List<SessionBlobSwap>();

        await connection.ExecuteAsync("BEGIN TRANSACTION");

        try
        {
            foreach (var board in data.Boards) await ApplyRemoteEntityAsync(connection, board);
            foreach (var bike in data.Bikes) await ApplyRemoteEntityAsync(connection, bike);
            foreach (var setup in data.Setups) await ApplyRemoteEntityAsync(connection, setup);
            foreach (var track in data.Tracks) await ApplyRemoteEntityAsync(connection, track);
            foreach (var session in data.Sessions)
            {
                var swap = await ApplyRemoteSessionAsync(connection, session);
                if (swap is not null)
                {
                    swaps.Add(swap);
                }
            }

            await connection.ExecuteAsync("COMMIT");
        }
        catch
        {
            await connection.ExecuteAsync("ROLLBACK");
            throw;
        }

        return swaps;
    }

    public async Task<long> GetLastSyncTimeAsync(string? serverUrl)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        var synchronization = await connection.Table<Synchronization>()
            .Where(sync => sync.ServerUrl == serverUrl)
            .FirstOrDefaultAsync();
        return synchronization?.LastSyncTime ?? 0;
    }

    public async Task UpdateLastSyncTimeAsync(string? serverUrl)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        var lastSyncTime = DateTimeOffset.Now.ToUnixTimeSeconds();
        var synchronization = await connection.Table<Synchronization>()
            .Where(sync => sync.ServerUrl == serverUrl)
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

    public async Task MergeAllAsync(SynchronizationData data)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        await connection.ExecuteAsync("BEGIN TRANSACTION");

        try
        {
            foreach (var bike in data.Bikes) await MergeAsync(connection, bike, MergeGenericAcceptedContentAsync);
            foreach (var setup in data.Setups) await MergeAsync(connection, setup, MergeGenericAcceptedContentAsync);
            foreach (var board in data.Boards) await MergeAsync(connection, board, MergeGenericAcceptedContentAsync);
            foreach (var session in data.Sessions) await MergeAsync(connection, session, MergeSessionAcceptedContentAsync);
            foreach (var track in data.Tracks) await MergeAsync(connection, track, MergeGenericAcceptedContentAsync);

            await connection.ExecuteAsync("COMMIT");
        }
        catch
        {
            await connection.ExecuteAsync("ROLLBACK");
            throw;
        }
    }

    private static async Task<List<T>> GetChangedAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteAsyncConnection connection,
        long since) where T : Synchronizable, new()
    {
        if (typeof(T) == typeof(Session))
        {
            return (List<T>)(object)await GetChangedSessionsAsync(connection, since);
        }

        return await connection.Table<T>()
            .Where(entity => entity.Updated > since || (entity.Deleted != null && entity.Deleted > since))
            .ToListAsync();
    }

    private static Task<List<Session>> GetChangedSessionsAsync(SQLiteAsyncConnection connection, long since)
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

    private static async Task ApplyRemoteEntityAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteAsyncConnection connection,
        T entity) where T : Synchronizable, new()
    {
        var existing = await FindAsync<T>(connection, entity.Id);
        if (existing is null)
        {
            await InsertEntityAsync(connection, entity);
            return;
        }

        await UpdateEntityAsync(connection, entity);
    }

    private async Task<SessionBlobSwap?> ApplyRemoteSessionAsync(SQLiteAsyncConnection connection, Session session)
    {
        var existing = await FindAsync<Session>(connection, session.Id);
        if (existing is null)
        {
            await InsertEntityAsync(connection, session);
            return null;
        }

        if (!existing.HasProcessedData)
        {
            // No held BLOB: the fingerprint is just metadata, so write it normally.
            // The BLOB, if any, arrives via the session-data fill with a match check.
            await connection.ExecuteAsync(
                UpdateRemoteSessionMetadataSql,
                CreateRemoteSessionMetadataValues(session, session.Updated, session.ClientUpdated));
            return null;
        }

        // Held BLOB: defer the two BLOB-bound columns so the row keeps advertising
        // the fingerprint of the bytes it holds, and sync the rest immediately.
        await connection.ExecuteAsync(
            UpdateRemoteSessionMetadataExceptFingerprintSql,
            CreateRemoteSessionMetadataExceptFingerprintValues(session, session.Updated, session.ClientUpdated));

        return await TryBuildSwapAsync(
            connection,
            session.Id,
            existing.ProcessingFingerprintJson,
            session.ProcessingFingerprintJson);
    }

    private static long GetContentVersion(Synchronizable entity) => entity.ClientUpdated > 0
        ? entity.ClientUpdated
        : entity.Updated;

    private static async Task MergeAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteAsyncConnection connection,
        T entity,
        Func<SQLiteAsyncConnection, T, T?, long, bool, Task> applyAcceptedContentAsync) where T : Synchronizable, new()
    {
        var existing = await FindAsync<T>(connection, entity.Id);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (existing is null)
        {
            await applyAcceptedContentAsync(connection, entity, null, now, true);
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
            await UpdateEntityAsync(connection, existing);
            return;
        }

        if (entity.Deleted.HasValue)
        {
            if (entity.Deleted <= existingContentVersion)
            {
                existing.Updated = now;
                await UpdateEntityAsync(connection, existing);
                return;
            }

            existing.Deleted = entity.Deleted;
            existing.Updated = now;
            await UpdateEntityAsync(connection, existing);
            return;
        }

        // Some other client updated the row later and synced earlier. Keep the
        // local content, but advance the synchronization timestamp.
        if (existingContentVersion > entity.Updated)
        {
            existing.Updated = now;
            await UpdateEntityAsync(connection, existing);
            return;
        }

        await applyAcceptedContentAsync(connection, entity, existing, now, false);
    }

    private static Task PersistAcceptedEntityAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteAsyncConnection connection,
        T entity,
        long now,
        bool isInsert) where T : Synchronizable, new()
    {
        return PersistEntityWithServerTimestampsAsync(
            entity,
            updated: now,
            clientUpdated: entity.Updated,
            persistAsync: isInsert
                ? row => InsertEntityAsync(connection, row)
                : row => UpdateEntityAsync(connection, row));
    }

    private static async Task PersistEntityWithServerTimestampsAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
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

    private static Task MergeGenericAcceptedContentAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteAsyncConnection connection,
        T entity,
        T? existing,
        long now,
        bool isInsert) where T : Synchronizable, new()
    {
        return PersistAcceptedEntityAsync(connection, entity, now, isInsert);
    }

    private async Task MergeSessionMetadataAsync(
        SQLiteAsyncConnection connection,
        Session session,
        Session? existing,
        long now)
    {
        if (existing is { HasProcessedData: true } heldRow)
        {
            // Hub-side defer: when the hub already holds a BLOB, keep its fingerprint,
            // data, and BLOB-derived metrics so /session/data keeps advertising the
            // bytes it holds, and sync only the rest of the metadata. The hub runs no
            // session-data pull phase, so to receive a newer BLOB it records a
            // push-swap request and asks a client to upload it.
            await connection.ExecuteAsync(
                UpdateRemoteSessionMetadataExceptFingerprintSql,
                CreateRemoteSessionMetadataExceptFingerprintValues(session, now, session.Updated));
            await UpdateSessionBlobSwapRequestAsync(connection, session, heldRow);
            return;
        }

        // No held BLOB: write the fingerprint + data columns normally (the BLOB, if
        // any, arrives via the data-null fill with a match check), and drop any stale
        // push-swap request for this row.
        await connection.ExecuteAsync(
            UpdateRemoteSessionMetadataSql,
            CreateRemoteSessionMetadataValues(session, now, session.Updated));
        await ClearSessionBlobSwapRequestAsync(connection, session.Id);
    }

    private Task MergeSessionAcceptedContentAsync(
        SQLiteAsyncConnection connection,
        Session session,
        Session? existing,
        long now,
        bool isInsert) =>
        isInsert
            ? PersistAcceptedEntityAsync(connection, session, now, isInsert: true)
            : MergeSessionMetadataAsync(connection, session, existing, now);

    // Records (or clears) a hub-side push-swap request for a deferred, held-BLOB row.
    // It asks a client to upload a newer BLOB when the synced metadata advertises a
    // current-schema fingerprint that matches the hub's CURRENT database inputs while
    // the held BLOB's fingerprint does not. That is regression-safe (it never targets a
    // stale client BLOB) and an option-only difference does not trigger a swap (both
    // would match the DB inputs), so devices with different processing options do not
    // ping-pong; that dimension self-heals through each device's own recompute-on-open.
    // Exception: when the recorded source has not synced yet the hub cannot compute its
    // current inputs, so it records the accepted target as-is rather than drop the
    // metadata delta — the upload is still match-checked, so a non-matching BLOB is
    // ignored and the held BLOB is preserved until a matching one arrives.
    private async Task UpdateSessionBlobSwapRequestAsync(
        SQLiteAsyncConnection connection,
        Session incoming,
        Session heldRow)
    {
        if (StringComparer.Ordinal.Equals(incoming.ProcessingFingerprintJson, heldRow.ProcessingFingerprintJson))
        {
            await ClearSessionBlobSwapRequestAsync(connection, incoming.Id);
            return;
        }

        var incomingFingerprint = fingerprintService.Parse(incoming.ProcessingFingerprintJson);
        var heldFingerprint = fingerprintService.Parse(heldRow.ProcessingFingerprintJson);
        if (incomingFingerprint is null || heldFingerprint is null ||
            incomingFingerprint.SchemaVersion != fingerprintService.CurrentSchemaVersion ||
            heldFingerprint.SchemaVersion != fingerprintService.CurrentSchemaVersion)
        {
            await ClearSessionBlobSwapRequestAsync(connection, incoming.Id);
            return;
        }

        var currentDatabaseInputs = await TryComputeCurrentDatabaseInputsAsync(connection, incoming);
        if (currentDatabaseInputs is not null &&
            incomingFingerprint.MatchesDatabaseInputs(currentDatabaseInputs) &&
            !heldFingerprint.MatchesDatabaseInputs(currentDatabaseInputs))
        {
            await RecordSessionBlobSwapRequestAsync(connection, incoming.Id, incoming.ProcessingFingerprintJson!);
            return;
        }

        if (currentDatabaseInputs is null && !await SessionHasRecordedSourceAsync(connection, incoming.Id))
        {
            // The source payload travels after metadata sync. Preserve the accepted
            // target so the hub can request the matching BLOB once the source phase
            // fills session_recording_source; otherwise this metadata delta would be
            // lost when the client advances its sync watermark.
            await RecordSessionBlobSwapRequestAsync(connection, incoming.Id, incoming.ProcessingFingerprintJson!);
            return;
        }

        await ClearSessionBlobSwapRequestAsync(connection, incoming.Id);
    }

    private async Task<ProcessingFingerprint?> TryComputeCurrentDatabaseInputsAsync(
        SQLiteAsyncConnection connection,
        Session session)
    {
        if (session.Setup is not { } setupId)
        {
            return null;
        }

        var setup = await connection.FindAsync<Setup>(setupId);
        if (setup is null)
        {
            return null;
        }

        var bike = await connection.FindAsync<Bike>(setup.BikeId);
        if (bike is null)
        {
            return null;
        }

        var sources = await connection.QueryAsync<RecordedSessionSource>(
            "SELECT session_id, source_kind, source_name, schema_version, source_hash FROM session_recording_source WHERE session_id = ?",
            session.Id);
        if (sources.Count != 1)
        {
            return null;
        }

        try
        {
            return fingerprintService.CreateCurrentDatabaseInputs(
                SessionSnapshot.From(session),
                SetupSnapshot.From(setup, null),
                BikeSnapshot.From(bike),
                RecordedSessionSourceSnapshot.From(sources[0]));
        }
        catch (InvalidOperationException)
        {
            // The session/setup/bike/source relationships are not coherent yet, so the
            // canonical fingerprint cannot be determined: do not request a swap.
            return null;
        }
    }

    private static Task ClearSessionBlobSwapRequestAsync(SQLiteAsyncConnection connection, Guid sessionId) =>
        connection.ExecuteAsync(
            $"DELETE FROM {SessionBlobSwapRequestStore.TableName} WHERE session_id = ?",
            sessionId);

    private static Task RecordSessionBlobSwapRequestAsync(
        SQLiteAsyncConnection connection,
        Guid sessionId,
        string targetFingerprint) =>
        connection.ExecuteAsync(
            $"INSERT OR REPLACE INTO {SessionBlobSwapRequestStore.TableName} (session_id, target_fingerprint) VALUES (?, ?)",
            sessionId,
            targetFingerprint);

    private static string? SerializeTrack(Session session) =>
        session.Track is null ? null : AppJson.Serialize(session.Track);

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
        NormalizeGpsOffsetSeconds(session.GpsOffsetSeconds),
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

    // Same column order as CreateRemoteSessionMetadataValues but WITHOUT the
    // session_processing_fingerprint value and the four BLOB-derived metric values
    // (the SQL omits those assignments — they are deferred with the BLOB).
    private static object?[] CreateRemoteSessionMetadataExceptFingerprintValues(
        Session session,
        long updated,
        long clientUpdated) =>
    [
        session.Name,
        session.Setup,
        session.Description,
        session.Timestamp,
        session.FullTrack,
        NormalizeGpsOffsetSeconds(session.GpsOffsetSeconds),
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

    private static double NormalizeGpsOffsetSeconds(double gpsOffsetSeconds) =>
        double.IsFinite(gpsOffsetSeconds) ? gpsOffsetSeconds : 0;

    // Decides whether a deferred (held-BLOB) row should queue a swap toward the
    // accepted remote fingerprint. Swaps only between two CURRENT-SCHEMA
    // fingerprints that differ, and never for a source-less row (it cannot
    // self-heal by recompute, so it keeps its only BLOB and only ever gains one
    // through the data-null fill). A legacy fingerprint on either side defers to
    // the one-time normalization pass.
    private async Task<SessionBlobSwap?> TryBuildSwapAsync(
        SQLiteAsyncConnection connection,
        Guid sessionId,
        string? localFingerprintJson,
        string? remoteFingerprintJson)
    {
        if (StringComparer.Ordinal.Equals(localFingerprintJson, remoteFingerprintJson))
        {
            return null;
        }

        var local = fingerprintService.Parse(localFingerprintJson);
        var remote = fingerprintService.Parse(remoteFingerprintJson);
        if (local is null || remote is null ||
            local.SchemaVersion != fingerprintService.CurrentSchemaVersion ||
            remote.SchemaVersion != fingerprintService.CurrentSchemaVersion)
        {
            return null;
        }

        if (!await SessionHasRecordedSourceAsync(connection, sessionId))
        {
            return null;
        }

        return new SessionBlobSwap(sessionId, remoteFingerprintJson!);
    }

    private static async Task<bool> SessionHasRecordedSourceAsync(SQLiteAsyncConnection connection, Guid sessionId)
    {
        var exists = await connection.ExecuteScalarAsync<int>(
            "SELECT EXISTS(SELECT 1 FROM session_recording_source WHERE session_id = ?)",
            sessionId);
        return exists != 0;
    }

    private static async Task<T?> FindAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteAsyncConnection connection,
        object primaryKey) where T : new()
    {
        return await connection.FindAsync<T>(primaryKey);
    }

}
