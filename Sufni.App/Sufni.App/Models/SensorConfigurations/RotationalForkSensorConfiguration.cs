using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sufni.App.Models.SensorConfigurations;

public class RotationalForkSensorConfiguration : SensorConfiguration
{
    private double startAngle;
    private double strokeToTravel;
    private Bike? bike;
    private readonly double measurementToAngle = 2.0 / Math.PI * 4096;

    [JsonPropertyName("max_length")] public double MaxLength { get; set; }
    [JsonPropertyName("arm_length")] public double ArmLength { get; set; }
    [JsonPropertyName("type")] public override SensorType Type { get; } = SensorType.RotationalFork;
    [JsonIgnore] public override Func<ushort, double> MeasurementToTravel
    {
        get
        {
            return (measurement) =>
            {
                var measuredAngle = measurement * measurementToAngle;
                var stroke = MaxLength - (2.0 * ArmLength * Math.Cos(measuredAngle + startAngle));
                return stroke * strokeToTravel;
            };
        }
    }
    [JsonIgnore] public override double MaxTravel
    {
        get
        {
            Debug.Assert(bike is not null && bike.ForkStroke.HasValue, "bike is not null && bike.ForkStroke.HasValue");
            return bike.ForkStroke.Value * strokeToTravel;
        }
    }

    public static new ISensorConfiguration? FromJson(string json, Bike bike)
    {
        Debug.Assert(bike.Linkage is not null, "bike.Linkage is not null");

        var sc = JsonSerializer.Deserialize<RotationalForkSensorConfiguration>(json, SerializerOptions);
        if (sc is null) return null;

        sc.startAngle = Math.Acos(sc.MaxLength / 2.0 / sc.ArmLength);
        sc.strokeToTravel = Math.Sin(bike.HeadAngle * Math.PI / 180.0);
        sc.bike = bike;

        return sc;
    }
}
