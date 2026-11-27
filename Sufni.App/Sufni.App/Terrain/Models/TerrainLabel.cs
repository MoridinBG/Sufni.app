using System.Text.Json.Serialization;
using SQLite;
using Sufni.App.SyncAndPairing.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

// The roughness class or grade band that the user gives a range of an immutable recording source, in source-relative
// seconds. Class holds the integer value of the enum that Kind names.
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

    [JsonPropertyName("kind")]
    [Column("kind"), NotNull]
    public TerrainLabelKind Kind { get; set; }

    [JsonPropertyName("class")]
    [Column("class"), NotNull]
    public int Class { get; set; }

    [Ignore, JsonIgnore]
    public TerrainLabelClass LabelClass
    {
        get => Kind == TerrainLabelKind.Steepness
            ? new TerrainLabelClass.Steepness((GradeBand)Class)
            : new TerrainLabelClass.Roughness((RoughnessClass)Class);
        set => (Kind, Class) = value switch
        {
            TerrainLabelClass.Roughness roughness => (TerrainLabelKind.Roughness, (int)roughness.Class),
            TerrainLabelClass.Steepness steepness => (TerrainLabelKind.Steepness, (int)steepness.Band)
        };
    }
}
