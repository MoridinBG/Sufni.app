using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MessagePack;
using SQLite;

using Sufni.App.Bikes.Models;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Segments.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Setups.Models;
namespace Sufni.App.SyncAndPairing.Models;

// Authored row history and transport sequences; local_seq never leaves this database.
public class Synchronizable
{
    [JsonPropertyName("id")]
    [PrimaryKey]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("updated"), NotNull]
    [JsonPropertyName("updated")]
    [IgnoreMember]
    public long Updated { get; set; }

    [Column("version")]
    [JsonPropertyName("version")]
    [IgnoreMember]
    public long Version { get; set; } = 1;

    [Column("updated_by")]
    [JsonPropertyName("updated_by")]
    [IgnoreMember]
    public string UpdatedBy { get; set; } = string.Empty;

    [Column("local_seq")]
    [JsonIgnore]
    [IgnoreMember]
    public long LocalSequence { get; set; }

    [Column("sync_seq")]
    [JsonPropertyName("sync_seq")]
    [IgnoreMember]
    public long SyncSequence { get; set; }

    [Column("deleted")]
    [JsonPropertyName("deleted")]
    [IgnoreMember]
    public long? Deleted { get; set; }
}

[Table("sync")]
public class Synchronization
{
    [Column("server_url")]
    [PrimaryKey]
    public string? ServerUrl { get; set; }
    
    [Column("last_push_time")]
    public long LastPushTime { get; set; }

    [Column("last_pull_time")]
    public long LastPullTime { get; set; }
}

public class SynchronizationData
{
    [JsonPropertyName("upper_bound")]
    [JsonRequired]
    public long UpperBound { get; set; }

    // Wire bundle for one sync exchange. Session blobs and recorded sources
    // travel through separate endpoints so this stays focused on entity deltas.
    [JsonPropertyName("board")] public List<Board> Boards { get; set; } = [];
    [JsonPropertyName("bike")] public List<Bike> Bikes { get; set; } = [];
    [JsonPropertyName("setup")] public List<Setup> Setups { get; set; } = [];
    [JsonPropertyName("session")] public List<Session> Sessions { get; set; } = [];
    [JsonPropertyName("track")] public List<Track> Tracks { get; set; } = [];
    [JsonPropertyName("segment")] public List<SessionSegment> Segments { get; set; } = [];
    [JsonPropertyName("deletions")] public List<SynchronizationDeletion> Deletions { get; set; } = [];
    [JsonPropertyName("custom_tile_layers")] public List<CustomTileLayer> CustomTileLayers { get; set; } = [];
}
