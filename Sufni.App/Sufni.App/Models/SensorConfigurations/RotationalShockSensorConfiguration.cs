using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MathNet.Numerics;
using Sufni.Kinematics;

namespace Sufni.App.Models.SensorConfigurations;

public class RotationalShockSensorConfiguration : SensorConfiguration
{
    private readonly double measurementToAngle = 2.0 / Math.PI * 4096;
    private Bike? bike;
    private Polynomial? angleToTravelPolynomial;
    private BikeCharacteristics? characteristics;

    [JsonPropertyName("central_joint")] public string CentralJoint { get; init; } = null!;
    [JsonPropertyName("adjacent_joint_1")] public string AdjacentJoint1 { get; init; } = null!;
    [JsonPropertyName("adjacent_joint_2")] public string AdjacentJoint2 { get; init; } = null!;
    [JsonPropertyName("type")] public override SensorType Type { get; set; } = SensorType.RotationalShock;
    [JsonIgnore] public override Func<ushort, double> MeasurementToTravel
    {
        get
        {
            angleToTravelPolynomial ??= CalculateAngleToTravelPolynomial();
            return measurement => angleToTravelPolynomial.Evaluate(measurement * measurementToAngle);
        }
    }
    [JsonIgnore] public override double MaxTravel
    {
        get
        {
            characteristics ??= CalculateBikeCharacteristics();
            Debug.Assert(characteristics.MaxRearTravel.HasValue);

            return characteristics.MaxRearTravel.Value;
        }
    }

    public new static ISensorConfiguration? FromJson(string json, Bike bike)
    {
        Debug.Assert(bike.Linkage is not null);

        var sc = JsonSerializer.Deserialize<RotationalShockSensorConfiguration>(json, SerializerOptions);
        if (sc is null) return null;

        sc.bike = bike;

        return sc;
    }

    private BikeCharacteristics CalculateBikeCharacteristics()
    {
        Debug.Assert(bike?.Linkage != null);

        var solver = KinematicSolver.Create(500, bike.Linkage!);
        var solution = solver.SolveSuspensionMotion();
        return new BikeCharacteristics(solution, frontStroke: bike.ForkStroke, headAngle: bike.HeadAngle);
    }

    private Polynomial CalculateAngleToTravelPolynomial()
    {
        characteristics ??= CalculateBikeCharacteristics();
        var angleToTravelDataset = characteristics.AngleToTravelDataset(CentralJoint, AdjacentJoint1, AdjacentJoint2);
        return Polynomial.Fit([.. angleToTravelDataset.X], [.. angleToTravelDataset.Y], 3);
    }
}