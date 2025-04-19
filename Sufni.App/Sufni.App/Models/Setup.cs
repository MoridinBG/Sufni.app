using System;
using System.Text.Json.Serialization;
using SQLite;

namespace Sufni.App.Models;

[Table("setup")]
public class Setup : Synchronizable
{
    // Just to satisfy sql-net-pcl's parameterless constructor requirement
    // Uninitialized non-nullable property warnings are suppressed with null! initializer.
    public Setup()
    {
    }

    public Setup(Guid id, string name, Guid linkageId, Guid? frontCalibrationId, Guid? rearCalibrationId)
    {
        Id = id;
        Name = name;
        LinkageId = linkageId;
        FrontCalibrationId = frontCalibrationId;
        RearCalibrationId = rearCalibrationId;
    }

    [JsonPropertyName("id")]
    [PrimaryKey]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    [Column("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("linkage_id")]
    [Column("linkage_id")]
    public Guid LinkageId { get; set; }

    [JsonPropertyName("front_calibration_id")]
    [Column("front_calibration_id")]
    public Guid? FrontCalibrationId { get; set; }

    [JsonPropertyName("rear_calibration_id")]
    [Column("rear_calibration_id")]
    public Guid? RearCalibrationId { get; set; }
}