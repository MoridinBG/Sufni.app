using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using SQLite;
using Sufni.App.Models.SensorConfigurations;
using Sufni.Kinematics;

namespace Sufni.App.Models;

[Table("setup")]
public class Setup : Synchronizable
{
    private static readonly ILogger logger = Log.ForContext<Setup>();

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

    public string ToJson(Bike bike, Guid? boardId)
    {
        return AppJson.SerializeIndented(SetupExportModel.FromSetup(this, bike, boardId));
    }

    public static SetupImportPayload? FromJson(string json)
    {
        try
        {
            var model = AppJson.Deserialize<SetupExportModel>(json);
            return model?.ToPayload();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or LeverageRatioValidationException)
        {
            logger.Warning(ex, "Setup JSON deserialization failed");
            return null;
        }
    }
}

public sealed record SetupImportPayload(Setup Setup, Bike Bike, Guid? BoardId);

internal sealed class SetupExportModel
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = null!;

    [JsonPropertyName("board_id")]
    public Guid? BoardId { get; init; }

    [JsonPropertyName("front_sensor_configuration")]
    public string? FrontSensorConfigurationJson { get; init; }

    [JsonPropertyName("rear_sensor_configuration")]
    public string? RearSensorConfigurationJson { get; init; }

    [JsonPropertyName("bike")]
    public BikeExportModel Bike { get; init; } = null!;

    public static SetupExportModel FromSetup(Setup setup, Bike bike, Guid? boardId)
    {
        return new SetupExportModel
        {
            Name = setup.Name,
            BoardId = boardId,
            FrontSensorConfigurationJson = setup.FrontSensorConfigurationJson,
            RearSensorConfigurationJson = setup.RearSensorConfigurationJson,
            Bike = BikeExportModel.FromBike(bike)
        };
    }

    public SetupImportPayload ToPayload()
    {
        var bike = Bike.ToBike();
        var setup = new Setup(Guid.NewGuid(), Name)
        {
            BikeId = bike.Id,
            FrontSensorConfigurationJson = FrontSensorConfigurationJson,
            RearSensorConfigurationJson = RearSensorConfigurationJson
        };
        return new SetupImportPayload(setup, bike, BoardId);
    }
}
