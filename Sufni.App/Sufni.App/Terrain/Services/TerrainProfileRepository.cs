using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Services;

public interface ITerrainProfileRepository
{
    Task<bool> TryCommitAsync(Guid sessionId, TerrainProfileKey expectedKey, TerrainMaterialization result,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, TerrainProfile>> ReadAsync(IReadOnlyDictionary<Guid, TerrainProfileKey> expected,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the class summaries in one transaction. A row is written only while it holds a computed profile with the
    /// profile key of its class key. Returns the IDs of the written sessions.
    /// </summary>
    Task<IReadOnlyList<Guid>> CommitClassesAsync(
        IReadOnlyDictionary<Guid, (TerrainClassKey Key, TerrainClassSummary Summary)> classes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the label summaries in one transaction, with the guard of <see cref="CommitClassesAsync"/> on the profile
    /// key of each label key. Returns the IDs of the written sessions.
    /// </summary>
    Task<IReadOnlyList<Guid>> CommitLabelsAsync(
        IReadOnlyDictionary<Guid, (TerrainLabelKey Key, TerrainLabelSummary Summary)> labels,
        CancellationToken cancellationToken = default);
}

internal sealed class TerrainProfileRepository(SqliteConnectionContext context) : ITerrainProfileRepository
{
    public Task<bool> TryCommitAsync(Guid sessionId, TerrainProfileKey expectedKey, TerrainMaterialization result,
        CancellationToken cancellationToken = default)
    {
        var bytes = result.Profile is { } profile
            ? MessagePackSerializer.Serialize(profile, cancellationToken: cancellationToken) : null;
        var summary = result.Summary is null ? null : AppJson.Serialize(result.Summary);
        return context.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = connection.FindWithQuery<Session>("""
                SELECT processed_telemetry_revision, timestamp, duration_seconds, gps_offset_seconds, window_start_seconds
                FROM session WHERE id = ? AND deleted IS NULL AND data IS NOT NULL
                """, sessionId);
            if (session is null) return false;
            var content = SessionContentToken.Create(SessionSnapshot.From(session),
                TrackRepository.ReadSnapshotsInTransaction(connection), out _);
            if (TerrainProfileKey.From(content) != expectedKey) return false;

            connection.Execute("""
                INSERT OR REPLACE INTO session_terrain_profile
                (session_id, terrain_profile_key, terrain_outcome, terrain_summary, profile)
                VALUES (?, ?, ?, ?, ?)
                """, sessionId, AppJson.Serialize(expectedKey), (int)result.Outcome, summary, bytes);
            return true;
        }, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, TerrainProfile>> ReadAsync(
        IReadOnlyDictionary<Guid, TerrainProfileKey> expected, CancellationToken cancellationToken = default)
    {
        var connection = await context.GetInitializedConnectionAsync().WaitAsync(cancellationToken);
        var profiles = new Dictionary<Guid, TerrainProfile>();
        foreach (var (id, key) in expected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await connection.ExecuteScalarAsync<byte[]?>("""
                SELECT profile FROM session_terrain_profile
                WHERE session_id = ? AND terrain_profile_key = ? AND terrain_outcome = ?
                """, id, AppJson.Serialize(key), (int)TerrainOutcome.Computed);
            if (bytes is not null)
                profiles.Add(id, MessagePackSerializer.Deserialize<TerrainProfile>(bytes, cancellationToken: cancellationToken));
        }
        return profiles;
    }

    public Task<IReadOnlyList<Guid>> CommitClassesAsync(
        IReadOnlyDictionary<Guid, (TerrainClassKey Key, TerrainClassSummary Summary)> classes,
        CancellationToken cancellationToken = default) =>
        context.RunInTransactionAsync<IReadOnlyList<Guid>>(connection =>
        {
            var written = new List<Guid>();
            foreach (var (id, (key, summary)) in classes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rows = connection.Execute("""
                    UPDATE session_terrain_profile SET terrain_classes_key = ?, terrain_classes = ?
                    WHERE session_id = ? AND terrain_profile_key = ? AND terrain_outcome = ?
                    """, AppJson.Serialize(key), AppJson.Serialize(summary), id, AppJson.Serialize(key.Profile),
                    (int)TerrainOutcome.Computed);
                if (rows == 1) written.Add(id);
            }
            return written;
        }, cancellationToken);

    public Task<IReadOnlyList<Guid>> CommitLabelsAsync(
        IReadOnlyDictionary<Guid, (TerrainLabelKey Key, TerrainLabelSummary Summary)> labels,
        CancellationToken cancellationToken = default) =>
        context.RunInTransactionAsync<IReadOnlyList<Guid>>(connection =>
        {
            var written = new List<Guid>();
            foreach (var (id, (key, summary)) in labels)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rows = connection.Execute("""
                    UPDATE session_terrain_profile SET terrain_labels_key = ?, terrain_labels = ?
                    WHERE session_id = ? AND terrain_profile_key = ? AND terrain_outcome = ?
                    """, AppJson.Serialize(key), AppJson.Serialize(summary), id, AppJson.Serialize(key.Profile),
                    (int)TerrainOutcome.Computed);
                if (rows == 1) written.Add(id);
            }
            return written;
        }, cancellationToken);
}
