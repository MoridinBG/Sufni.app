using System;
using System.Text.Json.Serialization;
using SQLite;

using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.Segments.Models;

// A named range on an immutable recording source, in source-relative seconds.
[Table("session_segment")]
public class SessionSegment : Synchronizable
{
    [Indexed]
    [JsonPropertyName("owner_key")]
    [Column("owner_key"), NotNull]
    public string OwnerKey { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    [Column("name"), NotNull]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("start_seconds")]
    [Column("start_seconds"), NotNull]
    public double StartSeconds { get; set; }

    [JsonPropertyName("end_seconds")]
    [Column("end_seconds"), NotNull]
    public double EndSeconds { get; set; }
}
