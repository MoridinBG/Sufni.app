using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Infrastructure;
using Sufni.App.Setups.Models;

namespace Sufni.App.Setups.Services;

public static class SetupFileFormat
{
    public static string Serialize(Setup setup, Bike bike, Guid? boardId) =>
        AppJson.SerializeIndented(SetupExportModel.FromSetup(setup, bike, boardId));

    public static SetupImportPayload? Parse(string json) =>
        AppJson.Deserialize<SetupExportModel>(json)?.ToPayload();
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
    public BikeExportDocument Bike { get; init; } = null!;

    public static SetupExportModel FromSetup(Setup setup, Bike bike, Guid? boardId)
    {
        return new SetupExportModel
        {
            Name = setup.Name,
            BoardId = boardId,
            FrontSensorConfigurationJson = setup.FrontSensorConfigurationJson,
            RearSensorConfigurationJson = setup.RearSensorConfigurationJson,
            Bike = BikeExportDocument.FromBike(bike)
        };
    }

    public SetupImportPayload ToPayload()
    {
        var bike = BikeFileFormat.Parse(Bike)
                   ?? throw new JsonException("Unsupported bike export document schema version.");
        var setup = new Setup(Guid.NewGuid(), Name)
        {
            BikeId = bike.Id,
            FrontSensorConfigurationJson = FrontSensorConfigurationJson,
            RearSensorConfigurationJson = RearSensorConfigurationJson
        };
        return new SetupImportPayload(setup, bike, BoardId);
    }
}
