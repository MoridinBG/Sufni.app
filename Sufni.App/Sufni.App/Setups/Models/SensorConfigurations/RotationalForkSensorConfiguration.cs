using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

using Sufni.App.Bikes.Stores;
namespace Sufni.App.Setups.Models.SensorConfigurations;

public class RotationalForkSensorConfiguration : SensorConfiguration, ISensorConfiguration
{
    private double startAngle;
    private double strokeToTravel;
    private double? forkStroke;
    private readonly double measurementToAngle = 2.0 * Math.PI / 4096;

    [JsonPropertyName("max_length")] public double MaxLength { get; init; }
    [JsonPropertyName("arm_length")] public double ArmLength { get; init; }
    [JsonPropertyName("type")] public override SensorType Type { get; set; } = SensorType.RotationalFork;
    [JsonIgnore] public bool MeasurementWraps => true;
    [JsonIgnore]
    public Func<ushort, double> MeasurementToTravel
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
    public double MaxTravel
    {
        get
        {
            Debug.Assert(forkStroke != null);
            return forkStroke.Value * strokeToTravel;
        }
    }

    internal RotationalForkSensorConfiguration BindBike(BikeSnapshot bike)
    {
        startAngle = Math.Acos(MaxLength / 2.0 / ArmLength);
        strokeToTravel = Math.Sin(bike.HeadAngle * Math.PI / 180.0);
        forkStroke = bike.ForkStroke;
        return this;
    }
}
