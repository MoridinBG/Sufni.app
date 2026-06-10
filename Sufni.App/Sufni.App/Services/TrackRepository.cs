using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.Models;

namespace Sufni.App.Services;

public interface ITrackRepository
{
    Task<Guid?> FindTrackByTimeRangeAsync(long startTime, long endTime);

    Task<Guid?> AssociateSessionWithTrackAsync(Guid sessionId);

    Task<Guid?> FindTrackContainingTimestampAsync(long? timestamp);

    Task<List<Track>> GetTracksByIdsAsync(IReadOnlyCollection<Guid> trackIds);
}

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

    public async Task<Guid?> AssociateSessionWithTrackAsync(Guid sessionId)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();

        var sessions = await connection.QueryAsync<Session>(
            "SELECT id,timestamp FROM session WHERE deleted IS null AND id = ?", sessionId);
        if (sessions.Count == 0)
        {
            throw new Exception($"Session {sessionId} does not exist.");
        }

        var session = sessions[0];
        var trackId = await FindTrackContainingTimestampAsync(session.Timestamp);
        if (trackId is null) return null;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await connection.ExecuteAsync("UPDATE session SET full_track_id=?, updated=? WHERE id=?", trackId.Value, now, session.Id);
        return trackId;
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
            ORDER BY start_time DESC, end_time ASC, updated ASC, id ASC
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

    private sealed class TrackIdRow
    {
        [Column("id")]
        public Guid Id { get; set; }
    }
}
