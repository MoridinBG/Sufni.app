using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MathNet.Numerics;
using Sufni.Kinematics;

namespace Sufni.App.Models.SensorConfigurations;

public class LinearShockSensorConfiguration : SensorConfiguration
{
    private double measurementToStroke;
    private Bike? bike;
    private Polynomial? leverageRatioPolynomial;

    [JsonPropertyName("length")] public double Length { get; set; }
    [JsonPropertyName("resolution")] public int Resolution { get; set; }
    [JsonPropertyName("type")] public override SensorType Type { get; } = SensorType.LinearShock;
    [JsonIgnore] public override Func<ushort, double> MeasurementToTravel
    {
        get
        {
            leverageRatioPolynomial ??= CalculateLeverageRatioPolynomial();
            return (measurement) => leverageRatioPolynomial.Evaluate(measurement * measurementToStroke);
        }
    }
    [JsonIgnore] public override double MaxTravel
    {
        get
        {
            Debug.Assert(bike is not null && bike.ShockStroke.HasValue, "bike is not null && bike.ShockStroke.HasValue");
            leverageRatioPolynomial ??= CalculateLeverageRatioPolynomial();
            return leverageRatioPolynomial.Evaluate(bike.ShockStroke.Value);
        }
    }

    public static new ISensorConfiguration? FromJson(string json, Bike bike)
    {
        Debug.Assert(bike.Linkage is not null, "bike.Linkage is not null");

        var sc = JsonSerializer.Deserialize<LinearShockSensorConfiguration>(json, SerializerOptions);
        if (sc is null) return null;

        sc.bike = bike;
        sc.measurementToStroke = sc.Length / (2 ^ sc.Resolution);

        return sc;
    }

    private Polynomial CalculateLeverageRatioPolynomial()
    {
        Debug.Assert(bike is not null && bike.Linkage is not null, "bike is not null, and has Linkage");

        var solver = KinematicSolver.Create(500, bike.Linkage!);
        var solution = solver.SolveSuspensionMotion();
        var characteristics = new BikeCharacteristics(solution, frontStroke: bike.ForkStroke, headAngle: bike.HeadAngle);
        return Polynomial.Fit([.. characteristics.LeverageRatioData.X], [.. characteristics.LeverageRatioData.Y], 3);
    }
}