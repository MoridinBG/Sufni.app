using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
namespace Sufni.App.Setups.Models.SensorConfigurations;

public enum SensorType
{
    LinearFork,
    RotationalFork,
    LinearShock,
    RotationalShock,
    LinearShockStroke,
}

public interface ISensorConfiguration
{
    public SensorType Type { get; set; }
    public Func<ushort, double> MeasurementToTravel { get; }
    public bool MeasurementWraps { get; }
    [JsonIgnore] public double MaxTravel { get; }
}

[JsonConverter(typeof(SensorConfigurationJsonConverter))]
public class SensorConfiguration
{
    [JsonPropertyName("type")] public virtual SensorType Type { get; set; }

    public static ISensorConfiguration? FromJson(string json, Bike bike)
    {
        var s = AppJson.Deserialize<SensorConfiguration>(json);
        if (s is null) return null;

        return s.Type switch
        {
            SensorType.LinearFork => LinearForkSensorConfiguration.FromJson(json, bike),
            SensorType.RotationalFork => RotationalForkSensorConfiguration.FromJson(json, bike),
            _ => null
        };
    }

    public static SensorConfiguration? FromJson(string json)
    {
        // Single parse: the polymorphic converter peeks "type" and returns the concrete type.
        return AppJson.Deserialize<SensorConfiguration>(json);
    }

    public static string ToJson(SensorConfiguration configuration)
    {
        return configuration switch
        {
            LinearForkSensorConfiguration linearFork => AppJson.Serialize(linearFork),
            RotationalForkSensorConfiguration rotationalFork => AppJson.Serialize(rotationalFork),
            LinearShockSensorConfiguration linearShock => AppJson.Serialize(linearShock),
            RotationalShockSensorConfiguration rotationalShock => AppJson.Serialize(rotationalShock),
            _ => throw new JsonException($"Unsupported sensor configuration type '{configuration.GetType().Name}'.")
        };
    }
}