using System.Text.Json.Serialization;
using SQLite;
using Sufni.App.SyncAndPairing.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

// The roughness class that the user gives a range of an immutable recording source, in source-relative seconds.
[Table("terrain_label")]
public class TerrainLabel : Synchronizable
{
    [Indexed]
    [JsonPropertyName("owner_key")]
    [Column("owner_key"), NotNull]
    public string OwnerKey { get; set; } = string.Empty;

    [JsonPropertyName("start_seconds")]
    [Column("start_seconds"), NotNull]
    public double StartSeconds { get; set; }

    [JsonPropertyName("end_seconds")]
    [Column("end_seconds"), NotNull]
    public double EndSeconds { get; set; }

    [JsonPropertyName("roughness_class")]
    [Column("roughness_class"), NotNull]
    public RoughnessClass Class { get; set; }
}
