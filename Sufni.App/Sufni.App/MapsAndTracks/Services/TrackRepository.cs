using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Sufni.App.MapsAndTracks.Queries;
using Sufni.App.Sessions.Models;
using SQLite;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Stores;
namespace Sufni.App.MapsAndTracks.Services;

public interface ITrackRepository
{
    Task<bool> TryCommitGpsMetricsAsync(Guid sessionId, GpsMetricsKey expectedKey, GpsMetrics metrics,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackSnapshot>> GetTrackSnapshotsAsync(IReadOnlyCollection<Guid>? trackIds = null);

    Task<bool> DeleteIfUnselectedAsync(Guid trackId);

    Task<TrackPayloadMetadata?> GetTrackPayloadMetadataAsync(Guid trackId);

    Task<TrackPayload?> GetTrackPayloadAsync(Guid trackId, long pointsRevision);
}

public sealed record TrackPayloadMetadata(Guid Id, long PointsRevision);

public sealed record TrackPayload(Guid Id, long PointsRevision, IReadOnlyList<TrackPoint> Points);

internal sealed class TrackRepository(SqliteConnectionContext connectionContext, TimeProvider timeProvider) : ITrackRepository
{
    private const int TrackLookupChunkSize = 500;
    private const string ActiveTrackMetadataQuery = "SELECT id, start_time, end_time, points_revision FROM track WHERE deleted IS NULL";

    public Task<bool> TryCommitGpsMetricsAsync(Guid sessionId, GpsMetricsKey expectedKey, GpsMetrics metrics,
        CancellationToken cancellationToken = default) => connectionContext.RunInTransactionAsync(connection =>
    {
        var session = connection.FindWithQuery<Session>(
            "SELECT timestamp, duration_seconds, gps_offset_seconds, window_start_seconds FROM session WHERE id = ? AND deleted IS NULL", sessionId);
        if (session is null) return false;
        var tracks = ReadSnapshotsInTransaction(connection);
        var current = SessionGpsMetricsQuery.Evaluate(session.Timestamp, session.DurationSeconds,
            session.GpsOffsetSeconds, session.WindowStartSeconds, tracks);
        if (current.Key != expectedKey) return false;
        connection.Execute("""
            INSERT INTO session_gps_metrics (session_id, gps_metrics_key, distance_meters, ascent_meters, descent_meters)
            VALUES (?, ?, ?, ?, ?)
            ON CONFLICT(session_id) DO UPDATE SET gps_metrics_key=excluded.gps_metrics_key,
                distance_meters=excluded.distance_meters, ascent_meters=excluded.ascent_meters, descent_meters=excluded.descent_meters
            """, sessionId, AppJson.Serialize(expectedKey), metrics.DistanceMeters, metrics.AscentMeters, metrics.DescentMeters);
        return true;
    }, cancellationToken);

    internal static void EnsureInTransaction(SQLiteConnection connection, Track track, long now)
    {
        var existing = connection.FindWithQuery<Track>(
            "SELECT id, deleted FROM track WHERE id = ?", track.Id);
        if (existing is null)
        {
            track.Updated = now;
            track.Deleted = null;
            PersistenceGuards.InsertEntity(connection, track);
        }
        else if (existing.Deleted is not null)
        {
            connection.Execute("UPDATE track SET deleted = NULL, updated = ? WHERE id = ?", now, track.Id);
        }
    }

    public async Task<IReadOnlyList<TrackSnapshot>> GetTrackSnapshotsAsync(IReadOnlyCollection<Guid>? trackIds = null)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        if (trackIds is null)
            return (await connection.QueryAsync<TrackPayloadMetadataRow>(ActiveTrackMetadataQuery)).Select(row => row.ToSnapshot()).ToArray();

        var snapshots = new List<TrackSnapshot>(trackIds.Count);
        foreach (var chunk in trackIds.Chunk(TrackLookupChunkSize))
        {
            var placeholders = string.Join(", ", chunk.Select(_ => "?"));
            var args = chunk.Select(id => (object)id.ToString("D")).ToArray();
            var rows = await connection.QueryAsync<TrackPayloadMetadataRow>($"{ActiveTrackMetadataQuery} AND id IN ({placeholders})", args);
            snapshots.AddRange(rows.Select(row => row.ToSnapshot()));
        }
        return snapshots;
    }

    public Task<bool> DeleteIfUnselectedAsync(Guid trackId) => connectionContext.RunInTransactionAsync(connection =>
        DeleteIfUnselectedInTransaction(connection, trackId, timeProvider.GetUtcNow().ToUnixTimeSeconds(),
            ReadSnapshotsInTransaction(connection)));

    internal static IReadOnlyList<TrackSnapshot> ReadSnapshotsInTransaction(SQLiteConnection connection) =>
        connection.Query<TrackPayloadMetadataRow>(ActiveTrackMetadataQuery).Select(row => row.ToSnapshot()).ToArray();

    internal static bool DeleteIfUnselectedInTransaction(SQLiteConnection connection, Guid trackId, long now,
        IReadOnlyList<TrackSnapshot> tracks)
    {
        if (!tracks.Any(track => track.Id == trackId)) return false;
        var sessions = connection.Query<Session>(
            "SELECT timestamp, duration_seconds, gps_offset_seconds FROM session WHERE deleted IS NULL");
        if (sessions.Any(session => SessionTrackSelection.Select(tracks, session.Timestamp,
                session.DurationSeconds, session.GpsOffsetSeconds)?.Id == trackId)) return false;
        return connection.Execute("UPDATE track SET deleted = ? WHERE id = ? AND deleted IS NULL", now, trackId) > 0;
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

    private sealed class TrackPayloadMetadataRow
    {
        [Column("id")]
        public Guid Id { get; set; }

        [Column("points_revision")]
        public long PointsRevision { get; set; }

        [Column("start_time")]
        public double StartTime { get; set; }

        [Column("end_time")]
        public double EndTime { get; set; }

        public TrackPayloadMetadata ToMetadata() => new(Id, PointsRevision);
        public TrackSnapshot ToSnapshot() => new(Id, StartTime, EndTime, PointsRevision);
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
