using System;

using Sufni.App.Video.Models;
namespace Sufni.App.Video.Stores;

/// <summary>
/// Immutable view of one linked clip. <see cref="FileExists"/> is observed
/// when the snapshot is created; a refresh observes it again.
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
    public static SessionVideoSnapshot From(SessionVideoClip clip) => new(
        clip.Id,
        clip.OwnerKey,
        clip.Path,
        clip.StartTimestamp,
        clip.DurationSeconds,
        clip.AlignmentOffsetSeconds,
        clip.Added,
        System.IO.File.Exists(clip.Path));
}
