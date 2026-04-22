using System.Text.Json.Serialization;

namespace Sufni.App.Models.SensorConfigurations;

public sealed class LinearShockStrokeSensorConfiguration : SensorConfiguration
{
    [JsonPropertyName("length")] public double Length { get; init; }

    [JsonPropertyName("resolution")] public int Resolution { get; init; }

    [JsonPropertyName("type")] public override SensorType Type { get; set; } = SensorType.LinearShockStroke;
}