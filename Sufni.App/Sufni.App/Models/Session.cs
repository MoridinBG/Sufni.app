using System;
using System.Text.Json.Serialization;
using SQLite;

namespace Sufni.App.Models;

[Table("session")]
public class Session : Synchronizable
{
    private byte[]? processedData;

    // Just to satisfy sql-net-pcl's parameterless constructor requirement
    // Uninitialized non-nullable property warnings are suppressed with null! initializer.
    public Session() { }

    public Session(Guid id, string name, string description, Guid? setup, int? timestamp = null, Guid? track = null)
    {
        Id = id;
        Name = name;
        Description = description;
        Setup = setup;
        Timestamp = timestamp;
        Track = track;
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

    [JsonPropertyName("timestamp"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Column("timestamp")]
    public int? Timestamp { get; set; }

    [JsonPropertyName("track"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Column("track_id")]
    public Guid? Track { get; set; }

    [JsonIgnore]
    [Column("data")]
    public byte[]? ProcessedData
    {
        get => processedData;
        set
        {
            HasProcessedData = value is not null;
            processedData = value;
        }
    }

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
    [Column("has_data")]
    public bool HasProcessedData { get; set; }
}