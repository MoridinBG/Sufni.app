using System;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Sufni.App.Models;

namespace Sufni.App.Models.SensorConfigurations;

public class LinearForkSensorConfiguration : SensorConfiguration
{
    private double measurementToStroke;
    private double strokeToTravel;
    private Bike? bike;

    [JsonPropertyName("length")] public double Length { get; init; }
    [JsonPropertyName("resolution")] public int Resolution { get; init; }
    [JsonPropertyName("type")] public override SensorType Type { get; set; } = SensorType.LinearFork;
    [JsonIgnore] public override Func<ushort, double> MeasurementToTravel
    {
        get
        {
            return measurement => measurement * measurementToStroke * strokeToTravel;
        }
    }
    [JsonIgnore] public override double MaxTravel
    {
        get
        {
            Debug.Assert(bike?.ForkStroke != null);
            return bike.ForkStroke.Value * strokeToTravel;
        }
    }

    public new static ISensorConfiguration? FromJson(string json, Bike bike)
    {
        var sc = AppJson.Deserialize<LinearForkSensorConfiguration>(json);
        if (sc is null) return null;

        sc.bike = bike;
        sc.measurementToStroke = sc.Length / (2 ^ sc.Resolution);
        sc.strokeToTravel = Math.Sin(bike.HeadAngle * Math.PI / 180.0);
        return sc;
    }
}