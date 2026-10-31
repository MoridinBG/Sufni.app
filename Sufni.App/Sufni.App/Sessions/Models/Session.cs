using System;
using System.Text.Json.Serialization;
using SQLite;

using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.Sessions.Models;

[Table("session")]
public class Session : Synchronizable
{
    // Just to satisfy sql-net-pcl's parameterless constructor requirement
    // Uninitialized non-nullable property warnings are suppressed with null! initializer.
    public Session() { }

    public Session(Guid id, string name, string description, Guid? setup, long? timestamp = null)
    {
        Id = id;
        Name = name;
        Description = description;
        Setup = setup;
        Timestamp = timestamp;
    }

    [JsonPropertyName("name")]
    [Column("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("description")]
    [Column("description")]
    public string Description { get; set; } = null!;

    [JsonPropertyName("setup")]
    [Column("setup_id")]
    public Guid? Setup { get; set; }

    [JsonPropertyName("velocity_filter_window_ms"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Column("velocity_filter_window_ms")]
    public int? VelocityFilterWindowMilliseconds { get; set; }

    [JsonPropertyName("timestamp"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Column("timestamp")]
    public long? Timestamp { get; set; }

    [JsonPropertyName("duration_seconds"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Column("duration_seconds")]
    public double? DurationSeconds { get; set; }

    [JsonPropertyName("distance_meters"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Column("distance_meters")]
    public double? DistanceMeters { get; set; }

    [JsonPropertyName("ascent_meters"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Column("ascent_meters")]
    public double? AscentMeters { get; set; }

    [JsonPropertyName("descent_meters"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Column("descent_meters")]
    public double? DescentMeters { get; set; }

    [JsonPropertyName("full_track"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Column("full_track_id")]
    public Guid? FullTrack { get; set; }

    [JsonPropertyName("gps_offset_seconds")]
    [Column("gps_offset_seconds")]
    public double GpsOffsetSeconds { get; set; }

    // Derivation window: the session derives from [start, end) of the source
    // session's recording source. Null source means its own source, full range.
    [JsonPropertyName("source_session_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Column("source_session_id")]
    public Guid? SourceSessionId { get; set; }

    [JsonPropertyName("window_start_seconds")]
    [Column("window_start_seconds")]
    public double WindowStartSeconds { get; set; }

    [JsonPropertyName("window_end_seconds"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Column("window_end_seconds")]
    public double? WindowEndSeconds { get; set; }

    // The window before the last trim, kept for one-level undo.
    [JsonPropertyName("previous_window_start_seconds"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Column("previous_window_start_seconds")]
    public double? PreviousWindowStartSeconds { get; set; }

    [JsonPropertyName("previous_window_end_seconds"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Column("previous_window_end_seconds")]
    public double? PreviousWindowEndSeconds { get; set; }

    [JsonIgnore]
    [Ignore]
    public Guid RecordingSourceSessionId => SourceSessionId ?? Id;

    [JsonPropertyName("processing_fingerprint")]
    [Column("session_processing_fingerprint")]
    public string? ProcessingFingerprintJson { get; set; }

    [JsonIgnore]
    [Column("processed_telemetry_revision")]
    public long ProcessedTelemetryRevision { get; set; }

    [JsonIgnore]
    [Column("data")]
    public byte[]? ProcessedData { get; set; }

    [JsonPropertyName("front_springrate")]
    [Column("front_springrate")]
    public string? FrontSpringRate { get; set; }

    [JsonPropertyName("rear_springrate")]
    [Column("rear_springrate")]
    public string? RearSpringRate { get; set; }

    [JsonPropertyName("front_hsc")]
    [Column("front_hsc")]
    public uint? FrontHighSpeedCompression { get; set; }

    [JsonPropertyName("rear_hsc")]
    [Column("rear_hsc")]
    public uint? RearHighSpeedCompression { get; set; }

    [JsonPropertyName("front_lsc")]
    [Column("front_lsc")]
    public uint? FrontLowSpeedCompression { get; set; }

    [JsonPropertyName("rear_lsc")]
    [Column("rear_lsc")]
    public uint? RearLowSpeedCompression { get; set; }

    [JsonPropertyName("front_lsr")]
    [Column("front_lsr")]
    public uint? FrontLowSpeedRebound { get; set; }

    [JsonPropertyName("rear_lsr")]
    [Column("rear_lsr")]
    public uint? RearLowSpeedRebound { get; set; }

    [JsonPropertyName("front_hsr")]
    [Column("front_hsr")]
    public uint? FrontHighSpeedRebound { get; set; }

    [JsonPropertyName("rear_hsr")]
    [Column("rear_hsr")]
    public uint? RearHighSpeedRebound { get; set; }

    [JsonIgnore]
    [Ignore]
    public virtual bool HasProcessedData => ProcessedData is not null;
}
