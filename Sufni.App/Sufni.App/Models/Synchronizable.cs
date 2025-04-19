using System.Collections.Generic;
using System.Text.Json.Serialization;
using MessagePack;
using SQLite;
using Sufni.App.Models.Telemetry;

namespace Sufni.App.Models;

public class Synchronizable
{
    [Column("updated"), NotNull]
    [IgnoreMember]
    public int Updated { get; set; }
    [Column("deleted")]
    [IgnoreMember]
    public int? Deleted { get; set; }
}

[Table("sync")]
public class Synchronization
{
    [Column("last_sync_time")]
    [PrimaryKey]
    public int LastSyncTime { get; set; }
}

public class SynchronizationData
{
    [JsonPropertyName("board")] public List<Board> Boards { get; set; } = [];
    [JsonPropertyName("calibration_method")] public List<CalibrationMethod> CalibrationMethods { get; set; } = [];
    [JsonPropertyName("calibration")] public List<Calibration> Calibrations { get; set; } = [];
    [JsonPropertyName("linkage")] public List<Linkage> Linkages { get; set; } = [];
    [JsonPropertyName("setup")] public List<Setup> Setups { get; set; } = [];
    [JsonPropertyName("session")] public List<Session> Sessions { get; set; } = [];
}