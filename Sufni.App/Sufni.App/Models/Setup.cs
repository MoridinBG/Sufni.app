using System;
using System.Text.Json.Serialization;
using SQLite;
using Sufni.App.Models.SensorConfigurations;

namespace Sufni.App.Models;

[Table("setup")]
public class Setup : Synchronizable
{
    // Just to satisfy sql-net-pcl's parameterless constructor requirement
    // Uninitialized non-nullable property warnings are suppressed with null! initializer.
    public Setup()
    {
    }

    public Setup(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    [JsonPropertyName("name")]
    [Column("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("bike_id")]
    [Column("bike_id")]
    public Guid BikeId { get; set; }

    [JsonPropertyName("front_sensor_configuration")]
    [Column("front_sensor_configuration")]
    public string? FrontSensorConfigurationJson { get; set; }

    [JsonPropertyName("rear_sensor_configuration")]
    [Column("rear_sensor_configuration")]
    public string? RearSensorConfigurationJson { get; set; }

    public ISensorConfiguration? FrontSensorConfiguration(Bike bike)
    {
        return FrontSensorConfigurationJson is null ? null : SensorConfiguration.FromJson(FrontSensorConfigurationJson, bike);
    }

    public ISensorConfiguration? RearSensorConfiguration(Bike bike)
    {
        return RearSensorConfigurationJson is null ? null : SensorConfiguration.FromJson(RearSensorConfigurationJson, bike);
    }
}