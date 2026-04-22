using System.Text.Json.Serialization;

namespace Sufni.App.Models.SensorConfigurations;

public class RotationalShockSensorConfiguration : SensorConfiguration
{
    [JsonPropertyName("central_joint")] public string CentralJoint { get; init; } = null!;
    [JsonPropertyName("adjacent_joint_1")] public string AdjacentJoint1 { get; init; } = null!;
    [JsonPropertyName("adjacent_joint_2")] public string AdjacentJoint2 { get; init; } = null!;
    [JsonPropertyName("type")] public override SensorType Type { get; set; } = SensorType.RotationalShock;
}