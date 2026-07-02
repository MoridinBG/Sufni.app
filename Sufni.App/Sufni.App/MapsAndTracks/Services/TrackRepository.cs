using System;
using System.Collections.Generic;
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

    Task<TrackPayloadMetadata?> GetTrackPayloadMetadataAsync(Guid trackId);

    Task<TrackPayload?> GetTrackPayloadAsync(Guid trackId, long updated);
}

public sealed record TrackPayloadMetadata(Guid Id, long Updated);

public sealed record TrackPayload(Guid Id, long Updated, IReadOnlyList<TrackPoint> Points);

internal sealed class TrackRepository(SqliteConnectionContext connectionContext) : ITrackRepository
{
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

        foreach (var trackId in trackIds)
        {
            var track = await connection.Table<Track>()
                .Where(candidate => candidate.Id == trackId && candidate.Deleted == null)
                .FirstOrDefaultAsync();
            if (track is not null)
            {
                tracks.Add(track);
            }
        }

        return tracks;
    }

    public async Task<TrackPayloadMetadata?> GetTrackPayloadMetadataAsync(Guid trackId)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var rows = await connection.QueryAsync<TrackPayloadMetadataRow>(
            """
            SELECT id, updated
            FROM track
            WHERE deleted IS NULL AND id = ?
            """,
            trackId);
        return rows.Count == 1 ? rows[0].ToMetadata() : null;
    }

    public async Task<TrackPayload?> GetTrackPayloadAsync(Guid trackId, long updated)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var rows = await connection.QueryAsync<TrackPayloadRow>(
            """
            SELECT id, updated, points
            FROM track
            WHERE deleted IS NULL AND id = ? AND updated = ?
            """,
            trackId,
            updated);
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

        [Column("updated")]
        public long Updated { get; set; }

        public TrackPayloadMetadata ToMetadata() => new(Id, Updated);
    }

    private sealed class TrackPayloadRow
    {
        [Column("id")]
        public Guid Id { get; set; }

        [Column("updated")]
        public long Updated { get; set; }

        [Column("points")]
        public string PointsJson { get; set; } = null!;

        public TrackPayload? ToPayload()
        {
            var points = AppJson.Deserialize<List<TrackPoint>>(PointsJson);
            return points is null ? null : new TrackPayload(Id, Updated, points);
        }
    }
}
