using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using SQLite;
using Sufni.App.Infrastructure;
using Sufni.App.Video.Models;
namespace Sufni.App.Video.Services;

public interface ISessionVideoRepository
{
    /// <summary>
    /// Returns the clips of live sessions.
    /// </summary>
    Task<List<SessionVideoClip>> GetLiveAsync();

    /// <summary>
    /// Links one file to every given session in one transaction and returns
    /// the linked rows. A session that already links the same path keeps its
    /// clip id, added time, and alignment offset.
    /// </summary>
    Task<IReadOnlyList<SessionVideoClip>> LinkAsync(
        string path,
        long startTimestamp,
        double durationSeconds,
        long added,
        IReadOnlyCollection<Guid> sessionIds);

    /// <summary>
    /// Returns the updated row, or null when the clip no longer exists.
    /// </summary>
    Task<SessionVideoClip?> UpdateAlignmentAsync(Guid clipId, double alignmentOffsetSeconds);
}

internal sealed class SessionVideoRepository(SqliteConnectionContext connectionContext) : ISessionVideoRepository
{
    public async Task<List<SessionVideoClip>> GetLiveAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.QueryAsync<SessionVideoClip>(
            """
            SELECT clip.*
            FROM session_video clip
            JOIN session ON session.id = clip.session_id AND session.deleted IS NULL
            """);
    }

    public Task<IReadOnlyList<SessionVideoClip>> LinkAsync(
        string path,
        long startTimestamp,
        double durationSeconds,
        long added,
        IReadOnlyCollection<Guid> sessionIds)
    {
        return connectionContext.RunInTransactionAsync<IReadOnlyList<SessionVideoClip>>(connection =>
        {
            var clips = new List<SessionVideoClip>(sessionIds.Count);
            foreach (var sessionId in sessionIds)
            {
                var clip = connection.Table<SessionVideoClip>()
                    .Where(existing => existing.SessionId == sessionId && existing.Path == path)
                    .FirstOrDefault();
                if (clip is null)
                {
                    clip = new SessionVideoClip
                    {
                        Id = Guid.NewGuid(),
                        SessionId = sessionId,
                        Path = path,
                        StartTimestamp = startTimestamp,
                        DurationSeconds = durationSeconds,
                        Added = added,
                    };
                    connection.Insert(clip);
                }
                else
                {
                    clip.StartTimestamp = startTimestamp;
                    clip.DurationSeconds = durationSeconds;
                    connection.Update(clip);
                }

                clips.Add(clip);
            }

            return clips;
        });
    }

    public async Task<SessionVideoClip?> UpdateAlignmentAsync(Guid clipId, double alignmentOffsetSeconds)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var clip = await connection.FindAsync<SessionVideoClip>(clipId);
        if (clip is null)
        {
            return null;
        }

        clip.AlignmentOffsetSeconds = alignmentOffsetSeconds;
        await connection.UpdateAsync(clip);
        return clip;
    }

    internal static IReadOnlyList<SessionVideoClip> ShiftAlignmentInTransaction(
        SQLiteConnection connection,
        Guid sessionId,
        double deltaSeconds)
    {
        var clips = connection.Table<SessionVideoClip>().Where(clip => clip.SessionId == sessionId).ToList();
        foreach (var clip in clips)
        {
            clip.AlignmentOffsetSeconds += deltaSeconds;
            connection.Update(clip);
        }

        return clips;
    }

    internal static IReadOnlyList<SessionVideoClip> CopyToSessionInTransaction(
        SQLiteConnection connection,
        Guid fromSessionId,
        Guid toSessionId,
        double alignmentDeltaSeconds,
        long added)
    {
        var copies = connection.Table<SessionVideoClip>()
            .Where(clip => clip.SessionId == fromSessionId)
            .ToList()
            .Select(clip => new SessionVideoClip
            {
                Id = Guid.NewGuid(),
                SessionId = toSessionId,
                Path = clip.Path,
                StartTimestamp = clip.StartTimestamp,
                DurationSeconds = clip.DurationSeconds,
                AlignmentOffsetSeconds = clip.AlignmentOffsetSeconds + alignmentDeltaSeconds,
                Added = added,
            })
            .ToList();
        foreach (var copy in copies)
        {
            connection.Insert(copy);
        }

        return copies;
    }
}
