using System;
using SQLite;

namespace Sufni.App.Video.Models;

// A local video file linked to one recording source. Device-local and never
// synced: the path is meaningful only on this device. Start and duration come
// from the file header; the alignment offset maps source seconds into video seconds.
[Table("session_video")]
public sealed class SessionVideoClip
{
    [PrimaryKey]
    [Column("id")]
    public Guid Id { get; set; }

    [Indexed]
    [Column("owner_key"), NotNull]
    public string OwnerKey { get; set; } = string.Empty;

    [Column("path"), NotNull]
    public string Path { get; set; } = string.Empty;

    // Unix seconds.
    [Column("start_timestamp"), NotNull]
    public long StartTimestamp { get; set; }

    [Column("duration_seconds"), NotNull]
    public double DurationSeconds { get; set; }

    [Column("alignment_offset_seconds"), NotNull]
    public double AlignmentOffsetSeconds { get; set; }

    // Unix milliseconds. The latest added clip wins where clips overlap.
    [Column("added"), NotNull]
    public long Added { get; set; }
}
