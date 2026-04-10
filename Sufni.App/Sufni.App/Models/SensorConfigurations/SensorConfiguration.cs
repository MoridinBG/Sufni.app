using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sufni.App.Models;

namespace Sufni.App.Models.SensorConfigurations;

public enum SensorType
{
    LinearFork,
    RotationalFork,
    LinearShock,
    RotationalShock,
}

public interface ISensorConfiguration
{
    public SensorType Type { get; set; }
    public Func<ushort, double> MeasurementToTravel { get; }
    [JsonIgnore] public double MaxTravel { get; }
    public static abstract ISensorConfiguration? FromJson(string json, Bike bike);
}

public class SensorConfiguration : ISensorConfiguration
{
    [JsonPropertyName("type")] public virtual SensorType Type { get; set; }
    [JsonIgnore] public virtual Func<ushort, double> MeasurementToTravel { get; } = null!;
    [JsonIgnore] public virtual double MaxTravel { get; }

    public static ISensorConfiguration? FromJson(string json, Bike bike)
    {
        var s = AppJson.Deserialize<SensorConfiguration>(json);
        if (s is null) return null;

        return s.Type switch
        {
            SensorType.LinearFork => LinearForkSensorConfiguration.FromJson(json, bike),
            SensorType.RotationalFork => RotationalForkSensorConfiguration.FromJson(json, bike),
            SensorType.LinearShock => LinearShockSensorConfiguration.FromJson(json, bike),
            SensorType.RotationalShock => RotationalShockSensorConfiguration.FromJson(json, bike),
            _ => null
        };
    }

    public static ISensorConfiguration? FromJson(string json)
    {
        var s = AppJson.Deserialize<SensorConfiguration>(json);
        if (s is null) return null;
        ISensorConfiguration? x = s.Type switch
        {
            SensorType.LinearFork => AppJson.Deserialize<LinearForkSensorConfiguration>(json),
            SensorType.RotationalFork => AppJson.Deserialize<RotationalForkSensorConfiguration>(json),
            SensorType.LinearShock => AppJson.Deserialize<LinearShockSensorConfiguration>(json),
            SensorType.RotationalShock => AppJson.Deserialize<RotationalShockSensorConfiguration>(json),
            _ => null
        };

        return x;
    }

    public static string ToJson(ISensorConfiguration configuration)
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