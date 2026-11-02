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
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Bikes.Models;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Segments.Models;
using Sufni.App.Setups.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
namespace Sufni.App.SyncAndPairing.Services;

public enum SynchronizationDirection { Push, Pull }

public interface ISyncDataStore
{
    Task<long> GetLastPushSequenceAsync(PeerIdentity peer);

    Task<long> GetLastPullSequenceAsync(PeerIdentity peer);

    Task UpdateLastPushSequenceAsync(PeerIdentity peer, long upperBound);

    Task UpdateLastPullSequenceAsync(PeerIdentity peer, long upperBound);

    Task<string> GetDatabaseIncarnationAsync();

    Task ResetCursorsAsync();

    Task<long> GetSequenceUpperBoundAsync(SynchronizationDirection direction);

    Task<SynchronizationData> GetSynchronizationDataAsync(
        SynchronizationDirection direction,
        long sinceExclusive,
        long upperInclusive);

    /// <summary>
    /// Applies a pulled remote delta and returns affected identities and pending
    /// generation swaps. Swaps are re-derived if the run does not complete.
    /// </summary>
    Task<SynchronizationApplyResult> ApplyRemoteSynchronizationDataAsync(SynchronizationData data);

    Task<SynchronizationApplyResult> MergeAllAsync(SynchronizationData data);
}

internal sealed class SynchronizationMergeEngine(
    SqliteConnectionContext connectionContext,
    IProcessingFingerprintService fingerprintService) : ISyncDataStore
{
    private static readonly string SessionSynchronizationProjection = $"""
                                                                      id,
                                                                      name,
                                                                      setup_id,
                                                                      description,
                                                                      timestamp,
                                                                      velocity_filter_window_ms,
                                                                      duration_seconds,
                                                                       gps_offset_seconds,
                                                                       source_session_id,
                                                                       window_start_seconds,
                                                                       window_end_seconds,
                                                                       previous_window_start_seconds,
                                                                       previous_window_end_seconds,
                                                                       {SessionSqlProjection.ProcessingFingerprintColumn},
                                                                      front_springrate, front_hsc, front_lsc, front_lsr, front_hsr,
                                                                      rear_springrate, rear_hsc, rear_lsc, rear_lsr, rear_hsr,
                                                                      updated, version, updated_by, local_seq, sync_seq,
                                                                      deleted,
                                                                      {SessionSqlProjection.HasDataProjection}
                                                                      """;

    private const string RemoteSessionMetadataUpdateAssignments = """
                                                                  name=?,
                                                                  setup_id=?,
                                                                  description=?,
                                                                  timestamp=?,
                                                                  velocity_filter_window_ms=?,
                                                                  duration_seconds=?,
                                                                   gps_offset_seconds=?,
                                                                   session_processing_fingerprint=?,
                                                                  source_session_id=?,
                                                                  window_start_seconds=?,
                                                                  window_end_seconds=?,
                                                                  previous_window_start_seconds=?,
                                                                  previous_window_end_seconds=?,
                                                                  front_springrate=?, front_hsc=?, front_lsc=?, front_lsr=?, front_hsr=?,
                                                                  rear_springrate=?, rear_hsc=?, rear_lsc=?, rear_lsr=?, rear_hsr=?,
                                                                  updated=?,
                                                                  version=?, updated_by=?, local_seq=?, sync_seq=?,
                                                                  deleted=?
                                                                  """;

    private static readonly string UpdateRemoteSessionMetadataSql = $"""
                                                                     UPDATE session
                                                                     SET
                                                                         {RemoteSessionMetadataUpdateAssignments}
                                                                     WHERE
                                                                         id=?
                                                                     """;

    private const string RemoteSessionMetadataExceptProcessedGenerationAssignments = """
                                                                  name=?,
                                                                  setup_id=?,
                                                                  description=?,
                                                                  timestamp=?,
                                                                  velocity_filter_window_ms=?,
                                                                  gps_offset_seconds=?,
                                                                  source_session_id=?,
                                                                  window_start_seconds=?,
                                                                  window_end_seconds=?,
                                                                  previous_window_start_seconds=?,
                                                                  previous_window_end_seconds=?,
                                                                  front_springrate=?, front_hsc=?, front_lsc=?, front_lsr=?, front_hsr=?,
                                                                  rear_springrate=?, rear_hsc=?, rear_lsc=?, rear_lsr=?, rear_hsr=?,
                                                                  updated=?,
                                                                  version=?, updated_by=?, local_seq=?, sync_seq=?,
                                                                  deleted=?
                                                                  """;

    private static readonly string UpdateRemoteSessionMetadataExceptProcessedGenerationSql = $"""
                                                                     UPDATE session
                                                                     SET
                                                                         {RemoteSessionMetadataExceptProcessedGenerationAssignments}
                                                                     WHERE
                                                                         id=?
                                                                     """;

    public async Task<long> GetSequenceUpperBoundAsync(SynchronizationDirection direction)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var column = direction == SynchronizationDirection.Push ? "local_seq" : "sync_seq";
        return await connection.ExecuteScalarAsync<long>($"SELECT {column} FROM sync_sequence WHERE id=1");
    }

    public async Task<SynchronizationData> GetSynchronizationDataAsync(
        SynchronizationDirection direction,
        long sinceExclusive,
        long upperInclusive)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        var sequenceColumn = direction == SynchronizationDirection.Push ? "local_seq" : "sync_seq";
        var boards = await GetChangedAsync<Board>(connection, sequenceColumn, sinceExclusive, upperInclusive);
        var bikes = await GetChangedAsync<Bike>(connection, sequenceColumn, sinceExclusive, upperInclusive);
        var setups = await GetChangedAsync<Setup>(connection, sequenceColumn, sinceExclusive, upperInclusive);
        var sessions = await GetChangedAsync<Session>(connection, sequenceColumn, sinceExclusive, upperInclusive);
        var tracks = await GetChangedAsync<Track>(connection, sequenceColumn, sinceExclusive, upperInclusive);
        var customLayers = await GetChangedAsync<CustomTileLayer>(connection, sequenceColumn, sinceExclusive, upperInclusive);
        var segments = await GetChangedAsync<SessionSegment>(connection, sequenceColumn, sinceExclusive, upperInclusive);
        var deletions = await connection.QueryAsync<SynchronizationDeletion>(
            $"SELECT * FROM sync_deletion WHERE {sequenceColumn} > ? AND {sequenceColumn} <= ?",
            sinceExclusive, upperInclusive);

        return new SynchronizationData
        {
            UpperBound = upperInclusive,
            Boards = boards,
            Bikes = bikes,
            Setups = setups,
            Sessions = sessions,
            Tracks = tracks,
            Segments = segments,
            Deletions = deletions,
            CustomTileLayers = customLayers
        };
    }

    public Task<SynchronizationApplyResult> ApplyRemoteSynchronizationDataAsync(SynchronizationData data) =>
        connectionContext.RunInTransactionAsync(connection => MergeRows(connection, data, asHub: false));

    public async Task<long> GetLastPushSequenceAsync(PeerIdentity peer)
    {
        var synchronization = await GetSynchronizationAsync(peer);
        return synchronization?.LastPushSequence ?? 0;
    }

    public async Task<long> GetLastPullSequenceAsync(PeerIdentity peer)
    {
        var synchronization = await GetSynchronizationAsync(peer);
        return synchronization?.LastPullSequence ?? 0;
    }

    public Task UpdateLastPushSequenceAsync(PeerIdentity peer, long upperBound) =>
        UpdateSynchronizationAsync(peer, "last_push_sequence", upperBound);

    public Task UpdateLastPullSequenceAsync(PeerIdentity peer, long upperBound) =>
        UpdateSynchronizationAsync(peer, "last_pull_sequence", upperBound);

    private async Task<Synchronization?> GetSynchronizationAsync(PeerIdentity peer)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.Table<Synchronization>()
            .Where(sync => sync.CertificateThumbprint == peer.CertificateThumbprint && sync.DatabaseIncarnation == peer.DatabaseIncarnation)
            .FirstOrDefaultAsync();
    }

    private async Task UpdateSynchronizationAsync(PeerIdentity peer, string column, long upperBound)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        await connection.ExecuteAsync($"""
            INSERT INTO sync (hub_thumbprint, db_incarnation, {column}) VALUES (?, ?, ?)
            ON CONFLICT(hub_thumbprint, db_incarnation) DO UPDATE SET {column}=excluded.{column}
            """, peer.CertificateThumbprint, peer.DatabaseIncarnation, upperBound);
    }

    public async Task ResetCursorsAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        await connection.ExecuteAsync("DELETE FROM sync");
    }

    public async Task<string> GetDatabaseIncarnationAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.ExecuteScalarAsync<string>("SELECT db_incarnation FROM database_identity WHERE id=1");
    }

    public Task<SynchronizationApplyResult> MergeAllAsync(SynchronizationData data) =>
        connectionContext.RunInTransactionAsync(connection => MergeRows(connection, data, asHub: true));

    private SynchronizationApplyResult MergeRows(SQLiteConnection connection, SynchronizationData data, bool asHub)
    {
        foreach (var deletion in data.Deletions)
        {
            if (deletion.Deleted is null) throw new InvalidOperationException("A deletion must carry its authored deletion time.");
            switch (deletion.EntityTable)
            {
                case "bike": MergeDeletion<Bike>(connection, deletion, asHub); break;
                case "setup": MergeDeletion<Setup>(connection, deletion, asHub); break;
                case "board": MergeDeletion<Board>(connection, deletion, asHub); break;
                case "track": MergeDeletion<Track>(connection, deletion, asHub); break;
                case "session": MergeDeletion<Session>(connection, deletion, asHub); break;
                case "custom_tile_layer": MergeDeletion<CustomTileLayer>(connection, deletion, asHub); break;
                case "session_segment": MergeDeletion<SessionSegment>(connection, deletion, asHub); break;
                default: throw new InvalidOperationException("Unknown synchronized deletion table.");
            }
        }
        foreach (var row in data.Bikes) Merge(connection, row, asHub);
        foreach (var row in data.Setups) Merge(connection, row, asHub);
        foreach (var row in data.Boards) Merge(connection, row, asHub);
        foreach (var row in data.Tracks) Merge(connection, row, asHub);
        foreach (var row in data.CustomTileLayers) Merge(connection, row, asHub);
        var swaps = new List<SessionBlobSwap>();
        foreach (var row in data.Sessions)
            if (Merge(connection, row, asHub) is { } swap) swaps.Add(swap);
        foreach (var row in data.Segments) Merge(connection, row, asHub);
        return new SynchronizationApplyResult(
            AffectedIds(data.Bikes, data.Deletions), AffectedIds(data.Setups, data.Deletions),
            AffectedIds(data.Boards, data.Deletions), AffectedIds(data.Tracks, data.Deletions),
            AffectedIds(data.Sessions, data.Deletions), AffectedIds(data.Segments, data.Deletions),
            AffectedIds(data.CustomTileLayers, data.Deletions), swaps);
    }

    private static Guid[] AffectedIds<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        IEnumerable<T> rows, IEnumerable<SynchronizationDeletion> deletions) where T : Synchronizable, new() =>
        rows.Select(row => row.Id).Concat(deletions.Where(row => row.EntityTable == GetTableName<T>())
            .Select(row => row.Id)).Distinct().ToArray();

    private static async Task<List<T>> GetChangedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteAsyncConnection connection, string sequenceColumn, long sinceExclusive, long upperInclusive)
        where T : Synchronizable, new()
    {
        var bounds = $"{sequenceColumn} > ? AND {sequenceColumn} <= ?";
        if (typeof(T) == typeof(Session))
            return (List<T>)(object)(await connection.QueryAsync<SessionMetadataRow>(
                $"SELECT {SessionSynchronizationProjection} FROM session WHERE {bounds}", sinceExclusive, upperInclusive))
                .Cast<Session>().ToList();
        return await connection.QueryAsync<T>($"SELECT * FROM {GetTableName<T>()} WHERE {bounds}", sinceExclusive, upperInclusive);
    }

    private SessionBlobSwap? Merge<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteConnection connection, T incoming, bool asHub) where T : Synchronizable, new()
    {
        if (incoming.Deleted is not null)
        {
            MergeDeletion<T>(connection, SynchronizationDeletion.From(incoming, GetTableName<T>()), asHub);
            return null;
        }
        var deleted = ReadDeletion<T>(connection, incoming.Id);
        if (deleted is not null && incoming.Version <= deleted.Version) return null;
        var existing = Find<T>(connection, incoming.Id);
        if (existing is null || SynchronizationMergeOrder.Compare(incoming, existing) > 0)
        {
            PersistWithSequences(connection, incoming, asHub, () =>
            {
                if (existing is null) InsertEntity(connection, incoming);
                else if (incoming is Session session)
                {
                    if (existing is Session { HasProcessedData: true })
                        connection.Execute(UpdateRemoteSessionMetadataExceptProcessedGenerationSql,
                            CreateRemoteSessionMetadataExceptProcessedGenerationValues(session));
                    else connection.Execute(UpdateRemoteSessionMetadataSql, CreateRemoteSessionMetadataValues(session));
                }
                else UpdateEntity(connection, incoming);
            });
        }
        if (incoming is not Session incomingSession) return null;
        var current = Find<Session>(connection, incoming.Id)!;
        if (current.Deleted is not null) return null;
        var target = DecideProcessedGenerationTarget(connection, incomingSession, current);
        if (!current.HasProcessedData)
        {
            if (target is not null && (current.ProcessingFingerprintJson != target.TargetFingerprint || current.DurationSeconds != target.TargetGeneration.DurationSeconds))
                PersistWithSequences(connection, incoming, asHub, () => connection.Execute(
                    "UPDATE session SET session_processing_fingerprint=?, duration_seconds=?, local_seq=?, sync_seq=? WHERE id=?",
                    target.TargetFingerprint, target.TargetGeneration.DurationSeconds, incoming.LocalSequence, incoming.SyncSequence, incoming.Id));
            if (asHub) ClearSessionBlobSwapRequest(connection, incoming.Id);
            return null;
        }
        if (!asHub) return target;
        if (target is null) ClearSessionBlobSwapRequest(connection, incoming.Id);
        else RecordSessionBlobSwapRequest(connection, incomingSession);
        return null;
    }

    private static SynchronizationDeletion? ReadDeletion<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteConnection connection, Guid id) where T : Synchronizable, new() =>
        connection.FindWithQuery<SynchronizationDeletion>("SELECT * FROM sync_deletion WHERE entity_table=? AND id=?", GetTableName<T>(), id);

    private static void MergeDeletion<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteConnection connection, SynchronizationDeletion incoming, bool asHub) where T : Synchronizable, new()
    {
        var recorded = ReadDeletion<T>(connection, incoming.Id);
        if (recorded is not null && SynchronizationMergeOrder.Compare(incoming, recorded) <= 0) return;
        var current = Find<T>(connection, incoming.Id);
        PersistWithSequences(connection, incoming, asHub, () =>
        {
            SynchronizationWriteMetadata.RecordDeletion(connection, incoming);
            if (current is not null && SynchronizationMergeOrder.Compare(incoming, current) > 0)
                connection.Execute($"UPDATE {GetTableName<T>()} SET version=?, updated=?, updated_by=?, deleted=?, local_seq=?, sync_seq=? WHERE id=?",
                    incoming.Version, incoming.Updated, incoming.UpdatedBy, incoming.Deleted, incoming.LocalSequence, incoming.SyncSequence, incoming.Id);
        });
    }

    private static void PersistWithSequences(SQLiteConnection connection, Synchronizable incoming, bool asHub, Action persist)
    {
        var originalLocal = incoming.LocalSequence;
        var originalSync = incoming.SyncSequence;
        try
        {
            incoming.LocalSequence = SynchronizationWriteMetadata.NextLocalSequence(connection);
            if (asHub) incoming.SyncSequence = SynchronizationWriteMetadata.NextSyncSequence(connection);
            persist();
        }
        finally
        {
            incoming.LocalSequence = originalLocal;
            incoming.SyncSequence = originalSync;
        }
    }

    private ProcessingFingerprint? TryComputeCurrentDatabaseInputs(
        SQLiteConnection connection,
        Session session)
    {
        if (session.Setup is not { } setupId)
        {
            return null;
        }

        var setup = connection.Find<Setup>(setupId);
        if (setup is null || setup.Deleted is not null)
        {
            return null;
        }

        var bike = connection.Find<Bike>(setup.BikeId);
        if (bike is null || bike.Deleted is not null)
        {
            return null;
        }

        var sourceSessionId = session.RecordingSourceSessionId;
        var sources = connection.Query<RecordedSessionSource>(
            "SELECT session_id, source_kind, source_name, schema_version, source_hash FROM session_recording_source WHERE session_id = ?",
            sourceSessionId);
        if (sources.Count != 1)
        {
            return null;
        }

        try
        {
            return fingerprintService.CreateCurrentDatabaseInputs(new SessionProcessingInputBundle(
                new SessionProcessingInput(session.Id, setupId, SessionSnapshot.From(session).DerivationWindow, session.VelocityFilterWindowMilliseconds),
                new SetupProcessingInput(
                    setup.Id, setup.BikeId, setup.FrontSensorConfigurationJson, setup.RearSensorConfigurationJson),
                new BikeProcessingInput(
                    bike.Id, bike.HeadAngle, bike.ForkStroke, bike.ShockStroke, bike.RearSuspensionJson),
                RecordedSessionSourceSnapshot.From(sources[0])));
        }
        catch (InvalidOperationException)
        {
            // The session/setup/bike/source relationships are not coherent yet, so the
            // canonical fingerprint cannot be determined: do not request a swap.
            return null;
        }
    }

    private static void ClearSessionBlobSwapRequest(SQLiteConnection connection, Guid sessionId) =>
        connection.Execute(
            $"DELETE FROM {SessionBlobSwapRequestStore.TableName} WHERE session_id = ?",
            sessionId);

    private static void RecordSessionBlobSwapRequest(
        SQLiteConnection connection,
        Session incoming) =>
        connection.Execute(
            $"""
            INSERT INTO {SessionBlobSwapRequestStore.TableName} (session_id, target_fingerprint, target_generation) VALUES (?, ?, ?)
            ON CONFLICT(session_id) DO UPDATE SET target_fingerprint=excluded.target_fingerprint, target_generation=excluded.target_generation
            WHERE target_fingerprint IS NOT excluded.target_fingerprint OR target_generation IS NOT excluded.target_generation
            """,
            incoming.Id,
            incoming.ProcessingFingerprintJson,
            AppJson.Serialize(SessionProcessedGeneration.From(incoming)));

    private static object?[] CreateRemoteSessionMetadataValues(
        Session session) =>
    [
        session.Name,
        session.Setup,
        session.Description,
        session.Timestamp,
        session.VelocityFilterWindowMilliseconds,
        session.DurationSeconds,
        SessionTrackTimeline.NormalizeGpsOffsetSeconds(session.GpsOffsetSeconds),
        session.ProcessingFingerprintJson,
        session.SourceSessionId,
        session.WindowStartSeconds,
        session.WindowEndSeconds,
        session.PreviousWindowStartSeconds,
        session.PreviousWindowEndSeconds,
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
        session.Version, session.UpdatedBy, session.LocalSequence, session.SyncSequence,
        session.Deleted,
        session.Id
    ];

    private static object?[] CreateRemoteSessionMetadataExceptProcessedGenerationValues(
        Session session) =>
    [
        session.Name,
        session.Setup,
        session.Description,
        session.Timestamp,
        session.VelocityFilterWindowMilliseconds,
        SessionTrackTimeline.NormalizeGpsOffsetSeconds(session.GpsOffsetSeconds),
        session.SourceSessionId,
        session.WindowStartSeconds,
        session.WindowEndSeconds,
        session.PreviousWindowStartSeconds,
        session.PreviousWindowEndSeconds,
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
        session.Version, session.UpdatedBy, session.LocalSequence, session.SyncSequence,
        session.Deleted,
        session.Id
    ];

    private SessionBlobSwap? DecideProcessedGenerationTarget(
        SQLiteConnection connection, Session incoming, Session held)
    {
        if (incoming.Deleted is not null) return null;
        var decision = ProcessedGenerationAcceptance.DecideProcessedGenerationTarget(
            TryComputeCurrentDatabaseInputs(connection, held),
            fingerprintService.Parse(held.ProcessingFingerprintJson),
            fingerprintService.Parse(incoming.ProcessingFingerprintJson),
            held.HasProcessedData,
            SessionHasRecordedSource(connection, held.RecordingSourceSessionId),
            fingerprintService.CurrentSchemaVersion);
        return decision == ProcessedGenerationDecision.KeepLocal ? null :
            new SessionBlobSwap(incoming.Id, incoming.ProcessingFingerprintJson!, SessionProcessedGeneration.From(incoming));
    }

    private static bool SessionHasRecordedSource(SQLiteConnection connection, Guid sessionId)
    {
        var exists = connection.ExecuteScalar<int>(
            "SELECT EXISTS(SELECT 1 FROM session_recording_source WHERE session_id = ?)",
            sessionId);
        return exists != 0;
    }

    private static T? Find<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        SQLiteConnection connection,
        object primaryKey) where T : new()
    {
        if (typeof(T) == typeof(Session))
            return (T?)(object?)connection.FindWithQuery<SessionMetadataRow>(
                $"SELECT {SessionSynchronizationProjection} FROM session WHERE id=?", primaryKey);
        return connection.FindWithQuery<T>(
            $"SELECT id, version, updated, updated_by, deleted, local_seq, sync_seq FROM {GetTableName<T>()} WHERE id=?", primaryKey);
    }

}
