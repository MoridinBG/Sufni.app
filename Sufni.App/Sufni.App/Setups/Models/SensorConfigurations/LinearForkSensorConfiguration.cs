using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

using Sufni.App.Bikes.Stores;
namespace Sufni.App.Setups.Models.SensorConfigurations;

public class LinearForkSensorConfiguration : SensorConfiguration, ISensorConfiguration
{
    private double measurementToStroke;
    private double strokeToTravel;
    private double? forkStroke;

    [JsonPropertyName("length")] public double Length { get; init; }
    [JsonPropertyName("resolution")] public int Resolution { get; init; }
    [JsonPropertyName("type")] public override SensorType Type { get; set; } = SensorType.LinearFork;
    [JsonIgnore] public bool MeasurementWraps => false;
    [JsonIgnore]
    public Func<ushort, double> MeasurementToTravel
    {
        get
        {
            return measurement => measurement * measurementToStroke * strokeToTravel;
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

    internal LinearForkSensorConfiguration BindBike(BikeSnapshot bike)
    {
        forkStroke = bike.ForkStroke;
        measurementToStroke = LinearSensorCalibrationMath.MeasurementToStroke(Length, Resolution);
        strokeToTravel = Math.Sin(bike.HeadAngle * Math.PI / 180.0);
        return this;
    }
}
