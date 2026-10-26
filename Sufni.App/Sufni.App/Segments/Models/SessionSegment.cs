using System;
using System.Text.Json.Serialization;
using SQLite;

using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.Segments.Models;

// A named time range on one recorded session, in seconds from the session's
// telemetry start. It is live only while its session is live.
[Table("session_segment")]
public class SessionSegment : Synchronizable
{
    [Indexed]
    [JsonPropertyName("session_id")]
    [Column("session_id"), NotNull]
    public Guid SessionId { get; set; }

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
