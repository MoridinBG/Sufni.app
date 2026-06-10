using System.Text.Json.Serialization;

namespace Sufni.App.ExtensionHost.Models;

public class TrackPoint(
    double time,
    double x,
    double y,
    double? elevation,
    double? speed = null,
    byte? fixMode = null,
    byte? satellites = null,
    float? epe2d = null,
    float? epe3d = null)
{
    [JsonPropertyName("time")] public double Time { get; set; } = time;
    [JsonPropertyName("x")] public double X { get; set; } = x;
    [JsonPropertyName("y")] public double Y { get; set; } = y;
    [JsonPropertyName("ele")] public double? Elevation { get; set; } = elevation;
    [JsonPropertyName("spd")] public double? Speed { get; set; } = speed;
    [JsonPropertyName("fix")] public byte? FixMode { get; set; } = fixMode;
    [JsonPropertyName("sats")] public byte? Satellites { get; set; } = satellites;
    [JsonPropertyName("epe2d")] public float? Epe2d { get; set; } = epe2d;
    [JsonPropertyName("epe3d")] public float? Epe3d { get; set; } = epe3d;
}
