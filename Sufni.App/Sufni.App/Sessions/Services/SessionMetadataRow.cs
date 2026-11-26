using System.Text.Json.Serialization;
using SQLite;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;
using Sufni.App.Sessions.Models;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Infrastructure;

namespace Sufni.App.Sessions.Services;

/// <summary>Metadata projection with database-derived BLOB availability and no payload read.</summary>
internal sealed class SessionMetadataRow : Session
{
    [Column("processed_data_available")]
    public bool ProcessedDataAvailable { get; set; }

    [Column("distance_meters"), JsonIgnore]
    public double? StoredDistanceMeters { get => DistanceMeters; set => DistanceMeters = value; }

    [Column("ascent_meters"), JsonIgnore]
    public double? StoredAscentMeters { get => AscentMeters; set => AscentMeters = value; }

    [Column("descent_meters"), JsonIgnore]
    public double? StoredDescentMeters { get => DescentMeters; set => DescentMeters = value; }

    [Column("gps_metrics_key"), JsonIgnore]
    public string? GpsMetricsKeyJson
    {
        get => GpsMetricsKey is null ? null : AppJson.Serialize(GpsMetricsKey);
        set => GpsMetricsKey = string.IsNullOrEmpty(value) ? null : AppJson.Deserialize<GpsMetricsKey>(value);
    }

    [Column("terrain_profile_key"), JsonIgnore]
    public string? TerrainProfileKeyJson
    {
        get => TerrainProfileKey is null ? null : AppJson.Serialize(TerrainProfileKey);
        set => TerrainProfileKey = value is null ? null : AppJson.Deserialize<TerrainProfileKey>(value);
    }

    [Column("terrain_outcome"), JsonIgnore]
    public int? StoredTerrainOutcome
    {
        get => (int?)TerrainOutcome;
        set => TerrainOutcome = (TerrainOutcome?)value;
    }

    [Column("terrain_summary"), JsonIgnore]
    public string? TerrainSummaryJson
    {
        get => TerrainSummary is null ? null : AppJson.Serialize(TerrainSummary);
        set => TerrainSummary = value is null ? null : AppJson.Deserialize<TerrainSummary>(value);
    }

    [Column("terrain_classes_key"), JsonIgnore]
    public string? TerrainClassKeyJson
    {
        get => TerrainClassKey is null ? null : AppJson.Serialize(TerrainClassKey);
        set => TerrainClassKey = value is null ? null : AppJson.Deserialize<TerrainClassKey>(value);
    }

    [Column("terrain_classes"), JsonIgnore]
    public string? TerrainClassSummaryJson
    {
        get => TerrainClassSummary is null ? null : AppJson.Serialize(TerrainClassSummary);
        set => TerrainClassSummary = value is null ? null : AppJson.Deserialize<TerrainClassSummary>(value);
    }

    [Column("terrain_labels_key"), JsonIgnore]
    public string? TerrainLabelKeyJson
    {
        get => TerrainLabelKey is null ? null : AppJson.Serialize(TerrainLabelKey);
        set => TerrainLabelKey = value is null ? null : AppJson.Deserialize<TerrainLabelKey>(value);
    }

    [Column("terrain_labels"), JsonIgnore]
    public string? TerrainLabelSummaryJson
    {
        get => TerrainLabelSummary is null ? null : AppJson.Serialize(TerrainLabelSummary);
        set => TerrainLabelSummary = value is null ? null : AppJson.Deserialize<TerrainLabelSummary>(value);
    }

    [Ignore, JsonIgnore]
    public override bool HasProcessedData => ProcessedDataAvailable;
}
