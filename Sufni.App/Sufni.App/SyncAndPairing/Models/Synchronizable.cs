using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MessagePack;
using SQLite;
using Sufni.App.ExtensionHost.Contracts.Sync;

using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Setups.Models;
namespace Sufni.App.SyncAndPairing.Models;

// Base row shape for data that participates in cross-device sync. Updated and
// ClientUpdated are merge clocks; Deleted is a soft-delete marker.
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
    [JsonPropertyName("app_preferences")] public AppPreferencesSyncData? AppPreferences { get; set; }
    [JsonPropertyName("extension")] public List<ExtensionSyncEnvelope> ExtensionBatches { get; set; } = [];
}
