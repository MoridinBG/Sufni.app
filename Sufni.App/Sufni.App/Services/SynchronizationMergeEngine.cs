using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.Models;

namespace Sufni.App.Services;

public interface ISyncDataStore
{
    Task<long> GetLastSyncTimeAsync(string? serverUrl);

    Task UpdateLastSyncTimeAsync(string? serverUrl);

    Task<SynchronizationData> GetSynchronizationDataAsync(long since);

    Task ApplyRemoteSynchronizationDataAsync(SynchronizationData data);

    Task MergeAllAsync(SynchronizationData data);
}

internal sealed class SynchronizationMergeEngine(
    SqliteConnectionContext connectionContext,
    ITrackRepository trackRepository) : ISyncDataStore
{
    private const string SessionProcessingFingerprintColumn = "session_processing_fingerprint";
    private const string SessionHasDataProjection = """
                                                    CASE
                                                       WHEN data IS NOT NULL THEN 1
                                                       ELSE 0
                                                    END AS has_data
                                                    """;

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
                                                                      {SessionProcessingFingerprintColumn},
                                                                      track,
                                                                      front_springrate, front_hsc, front_lsc, front_lsr, front_hsr,
                                                                      rear_springrate, rear_hsc, rear_lsc, rear_lsr, rear_hsr,
                                                                      updated,
                                                                      client_updated,
                                                                      deleted,
                                                                      {SessionHasDataProjection}
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

    public async Task ApplyRemoteSynchronizationDataAsync(SynchronizationData data)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        await connection.ExecuteAsync("BEGIN TRANSACTION");

        try
        {
            foreach (var board in data.Boards) await ApplyRemoteEntityAsync(connection, board);
            foreach (var bike in data.Bikes) await ApplyRemoteEntityAsync(connection, bike);
            foreach (var setup in data.Setups) await ApplyRemoteEntityAsync(connection, setup);
            foreach (var track in data.Tracks) await ApplyRemoteEntityAsync(connection, track);
            foreach (var session in data.Sessions) await ApplyRemoteSessionAsync(connection, session);

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

    private static async Task ApplyRemoteSessionAsync(SQLiteAsyncConnection connection, Session session)
    {
        var existing = await FindAsync<Session>(connection, session.Id);
        if (existing is null)
        {
            await InsertEntityAsync(connection, session);
            return;
        }

        await connection.ExecuteAsync(
            UpdateRemoteSessionMetadataSql,
            CreateRemoteSessionMetadataValues(session, session.Updated, session.ClientUpdated));
    }

    private static long GetContentVersion(Synchronizable entity) => entity.ClientUpdated > 0
        ? entity.ClientUpdated
        : entity.Updated;

    private static async Task MergeAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteAsyncConnection connection,
        T entity,
        Func<SQLiteAsyncConnection, T, long, bool, Task> applyAcceptedContentAsync) where T : Synchronizable, new()
    {
        var existing = await FindAsync<T>(connection, entity.Id);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (existing is null)
        {
            await applyAcceptedContentAsync(connection, entity, now, true);
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

        await applyAcceptedContentAsync(connection, entity, now, false);
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
        long now,
        bool isInsert) where T : Synchronizable, new()
    {
        return PersistAcceptedEntityAsync(connection, entity, now, isInsert);
    }

    private static Task MergeSessionMetadataAsync(SQLiteAsyncConnection connection, Session session, long now)
    {
        return connection.ExecuteAsync(
            UpdateRemoteSessionMetadataSql,
            CreateRemoteSessionMetadataValues(session, now, session.Updated));
    }

    private static Task MergeSessionAcceptedContentAsync(
        SQLiteAsyncConnection connection,
        Session session,
        long now,
        bool isInsert) =>
        isInsert
            ? PersistAcceptedEntityAsync(connection, session, now, isInsert: true)
            : MergeSessionMetadataAsync(connection, session, now);

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

    private static async Task<T?> FindAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteAsyncConnection connection,
        object primaryKey) where T : new()
    {
        return await connection.FindAsync<T>(primaryKey);
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
