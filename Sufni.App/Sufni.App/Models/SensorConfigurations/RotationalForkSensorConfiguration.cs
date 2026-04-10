using System;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Sufni.App.Models;

namespace Sufni.App.Models.SensorConfigurations;

public class RotationalForkSensorConfiguration : SensorConfiguration
{
    private double startAngle;
    private double strokeToTravel;
    private Bike? bike;
    private readonly double measurementToAngle = 2.0 * Math.PI / 4096;

    [JsonPropertyName("max_length")] public double MaxLength { get; init; }
    [JsonPropertyName("arm_length")] public double ArmLength { get; init; }
    [JsonPropertyName("type")] public override SensorType Type { get; set; } = SensorType.RotationalFork;
    [JsonIgnore]
    public override Func<ushort, double> MeasurementToTravel
    {
        get
        {
            return measurement =>
            {
                var measuredAngle = measurement * measurementToAngle;
                var stroke = MaxLength - 2.0 * ArmLength * Math.Cos(measuredAngle + startAngle);
                return stroke * strokeToTravel;
            };
        }
    }
    [JsonIgnore]
    public override double MaxTravel
    {
        get
        {
            Debug.Assert(bike?.ForkStroke != null);
            return bike.ForkStroke.Value * strokeToTravel;
        }
    }

    public new static ISensorConfiguration? FromJson(string json, Bike bike)
    {
        Debug.Assert(bike.Linkage is not null);

        var sc = AppJson.Deserialize<RotationalForkSensorConfiguration>(json);
        if (sc is null) return null;

        sc.startAngle = Math.Acos(sc.MaxLength / 2.0 / sc.ArmLength);
        sc.strokeToTravel = Math.Sin(bike.HeadAngle * Math.PI / 180.0);
        sc.bike = bike;

        return sc;
    }
}
