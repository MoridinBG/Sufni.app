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
    /// Returns every source-owned clip.
    /// </summary>
    Task<List<SessionVideoClip>> GetLiveAsync();

    /// <summary>
    /// Links one file to each recording source in one transaction and returns
    /// the linked rows. A source that already links the same path keeps its
    /// clip id, added time, and alignment offset.
    /// </summary>
    Task<IReadOnlyList<SessionVideoClip>> LinkAsync(
        string path,
        long startTimestamp,
        double durationSeconds,
        long added,
        IReadOnlyCollection<SessionVideoOwner> owners);

    /// <summary>
    /// Returns the updated row, or null when the clip no longer exists.
    /// </summary>
    Task<SessionVideoClip?> UpdateAlignmentAsync(Guid clipId, double alignmentOffsetSeconds);
}

/// <summary>Captured identity and clock origin for a video's recording owner.</summary>
public sealed record SessionVideoOwner(string OwnerKey, double StartTimestamp);

internal sealed class SessionVideoRepository(SqliteConnectionContext connectionContext) : ISessionVideoRepository
{
    public async Task<List<SessionVideoClip>> GetLiveAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.QueryAsync<SessionVideoClip>(
            """
            SELECT clip.*
            FROM session_video clip
            """);
    }

    public Task<IReadOnlyList<SessionVideoClip>> LinkAsync(
        string path,
        long startTimestamp,
        double durationSeconds,
        long added,
        IReadOnlyCollection<SessionVideoOwner> owners)
    {
        return connectionContext.RunInTransactionAsync<IReadOnlyList<SessionVideoClip>>(connection =>
        {
            var clips = new List<SessionVideoClip>(owners.Count);
            foreach (var owner in owners.DistinctBy(owner => owner.OwnerKey))
            {
                var clip = connection.Table<SessionVideoClip>()
                    .Where(existing => existing.OwnerKey == owner.OwnerKey && existing.Path == path)
                    .FirstOrDefault();
                if (clip is null)
                {
                    clip = new SessionVideoClip
                    {
                        Id = Guid.NewGuid(),
                        OwnerKey = owner.OwnerKey,
                        AlignmentOffsetSeconds = owner.StartTimestamp - startTimestamp,
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

}
