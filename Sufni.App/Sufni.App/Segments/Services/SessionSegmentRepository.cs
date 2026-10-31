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
    /// Returns the live source-owned segments.
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

internal sealed class SessionSegmentRepository(SqliteConnectionContext connectionContext, TimeProvider timeProvider) : ISessionSegmentRepository
{
    public async Task<List<SessionSegment>> GetLiveAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.QueryAsync<SessionSegment>(
            """
            SELECT segment.*
            FROM session_segment segment
            WHERE segment.deleted IS NULL
            """);
    }

    public async Task InsertAsync(SessionSegment segment)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        segment.Updated = timeProvider.GetUtcNow().ToUnixTimeSeconds();
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

        var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        change(segment, now);
        segment.Updated = now;
        await connection.UpdateAsync(segment);
        return segment;
    }

}
