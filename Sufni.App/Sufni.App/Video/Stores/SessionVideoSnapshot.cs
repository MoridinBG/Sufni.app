using System;

using Sufni.App.Video.Models;
namespace Sufni.App.Video.Stores;

/// <summary>
/// Immutable view of one linked clip and its observed file presence.
/// </summary>
public sealed record SessionVideoSnapshot(
    Guid Id,
    string OwnerKey,
    string Path,
    long StartTimestamp,
    double DurationSeconds,
    double AlignmentOffsetSeconds,
    long Added,
    bool FileExists)
{
    public static SessionVideoSnapshot From(SessionVideoClip clip, bool fileExists) => new(
        clip.Id,
        clip.OwnerKey,
        clip.Path,
        clip.StartTimestamp,
        clip.DurationSeconds,
        clip.AlignmentOffsetSeconds,
        clip.Added,
        fileExists);
}
