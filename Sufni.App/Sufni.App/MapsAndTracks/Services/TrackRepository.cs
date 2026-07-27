using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.ExtensionHost.Contracts.Models;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
namespace Sufni.App.MapsAndTracks.Services;

public interface ITrackRepository
{
    Task<Guid?> FindTrackByTimeRangeAsync(long startTime, long endTime);

    Task<Guid?> FindTrackContainingTimestampAsync(long? timestamp);

    Task<List<Track>> GetTracksByIdsAsync(IReadOnlyCollection<Guid> trackIds);

    Task<List<Track>> GetTracksByIdsForSynchronizationAsync(
        IReadOnlyCollection<Guid> trackIds,
        long upperInclusive);

    Task<List<TrackPayloadMetadata>> GetTrackPayloadMetadataByIdsAsync(IReadOnlyCollection<Guid> trackIds);

    Task<TrackPayloadMetadata?> GetTrackPayloadMetadataAsync(Guid trackId);

    Task<TrackPayload?> GetTrackPayloadAsync(Guid trackId, long pointsRevision);
}

public sealed record TrackPayloadMetadata(Guid Id, long PointsRevision);

public sealed record TrackPayload(Guid Id, long PointsRevision, IReadOnlyList<TrackPoint> Points);

internal sealed class TrackRepository(SqliteConnectionContext connectionContext) : ITrackRepository
{
    private const int TrackLookupChunkSize = 500;

    public async Task<Guid?> FindTrackByTimeRangeAsync(long startTime, long endTime)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        var rows = await connection.QueryAsync<TrackIdRow>(
            """
            SELECT id
            FROM track
            WHERE deleted IS NULL AND start_time = ? AND end_time = ?
            ORDER BY updated ASC, id ASC
            LIMIT 1
            """,
            startTime,
            endTime);
        return rows.Count == 0 ? null : rows[0].Id;
    }

    public async Task<Guid?> FindTrackContainingTimestampAsync(long? timestamp)
    {
        if (!timestamp.HasValue)
        {
            return null;
        }

        var connection = await connectionContext.GetInitializedConnectionAsync();
        var rows = await connection.QueryAsync<TrackIdRow>(
            """
            SELECT id
            FROM track
            WHERE deleted IS NULL AND start_time <= ? AND ? <= end_time
            ORDER BY (end_time - start_time) ASC, start_time ASC, id ASC
            LIMIT 1
            """,
            timestamp.Value,
            timestamp.Value);
        return rows.Count == 0 ? null : rows[0].Id;
    }

    public async Task<List<Track>> GetTracksByIdsAsync(IReadOnlyCollection<Guid> trackIds)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var tracks = new List<Track>(trackIds.Count);

        foreach (var chunk in trackIds.Chunk(TrackLookupChunkSize))
        {
            var placeholders = string.Join(", ", chunk.Select(_ => "?"));
            var args = chunk.Select(id => (object)id.ToString("D")).ToArray();
            var chunkTracks = await connection.QueryAsync<Track>(
                $"SELECT * FROM track WHERE deleted IS NULL AND id IN ({placeholders})",
                args);
            tracks.AddRange(chunkTracks);
        }

        return tracks;
    }

    public async Task<List<Track>> GetTracksByIdsForSynchronizationAsync(
        IReadOnlyCollection<Guid> trackIds,
        long upperInclusive)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var tracks = new List<Track>(trackIds.Count);

        foreach (var chunk in trackIds.Chunk(TrackLookupChunkSize))
        {
            var placeholders = string.Join(", ", chunk.Select(_ => "?"));
            var args = new object[] { upperInclusive }
                .Concat(chunk.Select(id => (object)id.ToString("D")))
                .ToArray();
            var chunkTracks = await connection.QueryAsync<Track>(
                $"SELECT * FROM track WHERE deleted IS NULL AND updated <= ? AND id IN ({placeholders})",
                args);
            tracks.AddRange(chunkTracks);
        }

        return tracks;
    }

    public async Task<TrackPayloadMetadata?> GetTrackPayloadMetadataAsync(Guid trackId)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var rows = await connection.QueryAsync<TrackPayloadMetadataRow>(
            """
            SELECT id, points_revision
            FROM track
            WHERE deleted IS NULL AND id = ?
            """,
            trackId);
        return rows.Count == 1 ? rows[0].ToMetadata() : null;
    }

    public async Task<List<TrackPayloadMetadata>> GetTrackPayloadMetadataByIdsAsync(
        IReadOnlyCollection<Guid> trackIds)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var metadata = new List<TrackPayloadMetadata>(trackIds.Count);

        foreach (var chunk in trackIds.Chunk(TrackLookupChunkSize))
        {
            var placeholders = string.Join(", ", chunk.Select(_ => "?"));
            var args = chunk.Select(id => (object)id.ToString("D")).ToArray();
            var rows = await connection.QueryAsync<TrackPayloadMetadataRow>(
                $"SELECT id, points_revision FROM track WHERE deleted IS NULL AND id IN ({placeholders})",
                args);
            metadata.AddRange(rows.Select(row => row.ToMetadata()));
        }

        return metadata;
    }

    public async Task<TrackPayload?> GetTrackPayloadAsync(Guid trackId, long pointsRevision)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var rows = await connection.QueryAsync<TrackPayloadRow>(
            """
            SELECT id, points_revision, points
            FROM track
            WHERE deleted IS NULL AND id = ? AND points_revision = ?
            """,
            trackId,
            pointsRevision);
        return rows.Count == 1 ? rows[0].ToPayload() : null;
    }

    private sealed class TrackIdRow
    {
        [Column("id")]
        public Guid Id { get; set; }
    }

    private sealed class TrackPayloadMetadataRow
    {
        [Column("id")]
        public Guid Id { get; set; }

        [Column("points_revision")]
        public long PointsRevision { get; set; }

        public TrackPayloadMetadata ToMetadata() => new(Id, PointsRevision);
    }

    private sealed class TrackPayloadRow
    {
        [Column("id")]
        public Guid Id { get; set; }

        [Column("points_revision")]
        public long PointsRevision { get; set; }

        [Column("points")]
        public string PointsJson { get; set; } = null!;

        public TrackPayload? ToPayload()
        {
            var points = AppJson.Deserialize<List<TrackPoint>>(PointsJson);
            return points is null ? null : new TrackPayload(Id, PointsRevision, points);
        }
    }
}
