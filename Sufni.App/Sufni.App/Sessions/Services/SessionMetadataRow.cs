using System.Text.Json.Serialization;
using SQLite;
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

    [Ignore, JsonIgnore]
    public override bool HasProcessedData => ProcessedDataAvailable;
}
