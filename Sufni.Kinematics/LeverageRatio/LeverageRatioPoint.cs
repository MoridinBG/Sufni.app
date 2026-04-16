using System.Text.Json.Serialization;

namespace Sufni.Kinematics;

public sealed record LeverageRatioPoint(
    [property: JsonPropertyName("shock_travel_mm")] double ShockTravelMm,
    [property: JsonPropertyName("wheel_travel_mm")] double WheelTravelMm);