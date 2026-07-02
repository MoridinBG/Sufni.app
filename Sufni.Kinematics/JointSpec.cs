using System.Text.Json.Serialization;

namespace Sufni.Kinematics;

public sealed record JointSpec(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] JointType? Type,
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y);
