using System;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    [JsonIgnore] public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public static ISensorConfiguration? FromJson(string json, Bike bike)
    {
        var s = JsonSerializer.Deserialize<SensorConfiguration>(json, SerializerOptions);
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
        var s = JsonSerializer.Deserialize<SensorConfiguration>(json, SerializerOptions);
        if (s is null) return null;
        ISensorConfiguration? x = s.Type switch
        {
            SensorType.LinearFork => JsonSerializer.Deserialize<LinearForkSensorConfiguration>(json, SerializerOptions),
            SensorType.RotationalFork => JsonSerializer.Deserialize<RotationalForkSensorConfiguration>(json, SerializerOptions),
            SensorType.LinearShock => JsonSerializer.Deserialize<LinearShockSensorConfiguration>(json, SerializerOptions),
            SensorType.RotationalShock => JsonSerializer.Deserialize<RotationalShockSensorConfiguration>(json, SerializerOptions),
            _ => null
        };

        return x;
    }
}