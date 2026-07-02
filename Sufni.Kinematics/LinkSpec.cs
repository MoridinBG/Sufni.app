using System.Text.Json.Serialization;

namespace Sufni.Kinematics;

public sealed record LinkSpec(
    [property: JsonPropertyName("a")] string A,
    [property: JsonPropertyName("b")] string B);
