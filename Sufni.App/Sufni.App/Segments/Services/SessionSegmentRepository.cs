using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SQLite;

using Sufni.App.Infrastructure;
using Sufni.App.Segments.Models;
namespace Sufni.App.Segments.Services;

public interface ISessionSegmentRepository
{
    /// <summary>
    /// Returns the live segments of live sessions.
    /// </summary>
    Task<List<SessionSegment>> GetLiveAsync();

    Task InsertAsync(SessionSegment segment);

    /// <summary>
    /// Returns the updated row, or null when the segment is missing or deleted.
    /// </summary>
    Task<SessionSegment?> RenameAsync(Guid segmentId, string name);

    /// <summary>
    /// Returns the deleted row, or null when the segment is missing or already deleted.
    /// </summary>
    Task<SessionSegment?> DeleteAsync(Guid segmentId);
}

internal sealed class SessionSegmentRepository(SqliteConnectionContext connectionContext) : ISessionSegmentRepository
{
    public async Task<List<SessionSegment>> GetLiveAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.QueryAsync<SessionSegment>(
            """
            SELECT segment.*
            FROM session_segment segment
            JOIN session ON session.id = segment.session_id AND session.deleted IS NULL
            WHERE segment.deleted IS NULL
            """);
    }

    public async Task InsertAsync(SessionSegment segment)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        segment.Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await connection.InsertAsync(segment);
    }

    public Task<SessionSegment?> RenameAsync(Guid segmentId, string name) =>
        UpdateLiveAsync(segmentId, (segment, _) => segment.Name = name);

    public Task<SessionSegment?> DeleteAsync(Guid segmentId) =>
        UpdateLiveAsync(segmentId, (segment, now) => segment.Deleted = now);

    private async Task<SessionSegment?> UpdateLiveAsync(Guid segmentId, Action<SessionSegment, long> change)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var segment = await connection.FindAsync<SessionSegment>(segmentId);
        if (segment is null || segment.Deleted is not null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        change(segment, now);
        segment.Updated = now;
        await connection.UpdateAsync(segment);
        return segment;
    }

    internal static IReadOnlyList<SessionSegment> ShiftInTransaction(
        SQLiteConnection connection,
        Guid sessionId,
        double offsetSeconds,
        long now)
    {
        var segments = GetLiveInTransaction(connection, sessionId);
        foreach (var segment in segments)
        {
            segment.StartSeconds += offsetSeconds;
            segment.EndSeconds += offsetSeconds;
            segment.Updated = now;
            connection.Update(segment);
        }

        return segments;
    }

    /// <summary>
    /// Moves the segments that start at or after <paramref name="fromSeconds"/>
    /// to another session whose telemetry starts there.
    /// </summary>
    internal static IReadOnlyList<SessionSegment> MoveToSessionInTransaction(
        SQLiteConnection connection,
        Guid fromSessionId,
        Guid toSessionId,
        double fromSeconds,
        long now)
    {
        var segments = GetLiveInTransaction(connection, fromSessionId)
            .Where(segment => segment.StartSeconds >= fromSeconds)
            .ToList();
        foreach (var segment in segments)
        {
            segment.SessionId = toSessionId;
            segment.StartSeconds -= fromSeconds;
            segment.EndSeconds -= fromSeconds;
            segment.Updated = now;
            connection.Update(segment);
        }

        return segments;
    }

    private static List<SessionSegment> GetLiveInTransaction(SQLiteConnection connection, Guid sessionId) =>
        connection.Table<SessionSegment>()
            .Where(segment => segment.SessionId == sessionId && segment.Deleted == null)
            .ToList();
}
