using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MessagePack;
using SQLite;

namespace Sufni.App.Models;

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

    [Column("client_updated")]
    [JsonPropertyName("client_updated")]
    [IgnoreMember]
    public long ClientUpdated { get; set; }

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
    
    [Column("last_sync_time")]
    public long LastSyncTime { get; set; }
}

public class SynchronizationData
{
    [JsonPropertyName("board")] public List<Board> Boards { get; set; } = [];
    [JsonPropertyName("bike")] public List<Bike> Bikes { get; set; } = [];
    [JsonPropertyName("setup")] public List<Setup> Setups { get; set; } = [];
    [JsonPropertyName("session")] public List<Session> Sessions { get; set; } = [];
    [JsonPropertyName("track")] public List<Track> Tracks { get; set; } = [];
}