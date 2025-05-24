using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MathNet.Numerics;
using Sufni.Kinematics;

namespace Sufni.App.Models.SensorConfigurations;

public class RotationalShockSensorConfiguration : SensorConfiguration
{
    private readonly double measurementToAngle = 2.0 * Math.PI / 4096;
    private double startAngle;
    private Bike? bike;
    private Polynomial? angleToTravelPolynomial;
    private bool anglesIncreasing;
    private BikeCharacteristics? characteristics;

    [JsonPropertyName("central_joint")] public string CentralJoint { get; init; } = null!;
    [JsonPropertyName("adjacent_joint_1")] public string AdjacentJoint1 { get; init; } = null!;
    [JsonPropertyName("adjacent_joint_2")] public string AdjacentJoint2 { get; init; } = null!;
    [JsonPropertyName("type")] public override SensorType Type { get; set; } = SensorType.RotationalShock;
    [JsonIgnore] public override Func<ushort, double> MeasurementToTravel
    {
        get
        {
            if (angleToTravelPolynomial is null) CalculateAngleToTravelPolynomial();
            Debug.Assert(angleToTravelPolynomial is not null);

            return measurement =>
            {
                var measuredAngle = measurement * measurementToAngle;
                if (!anglesIncreasing) measuredAngle = -measuredAngle;
                return angleToTravelPolynomial.Evaluate(startAngle + measuredAngle);
            };
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

        var central = bike.Linkage.Joints.First(j => j.Name == sc.CentralJoint);
        var adjacent1 = bike.Linkage.Joints.First(j => j.Name == sc.AdjacentJoint1);
        var adjacent2 = bike.Linkage.Joints.First(j => j.Name == sc.AdjacentJoint2);
        sc.startAngle = GeometryUtils.CalculateAngleAtPoint(central, adjacent1, adjacent2);

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

    private void CalculateAngleToTravelPolynomial()
    {
        characteristics ??= CalculateBikeCharacteristics();
        var angleToTravelDataset = characteristics.AngleToTravelDataset(CentralJoint, AdjacentJoint1, AdjacentJoint2);
        anglesIncreasing = angleToTravelDataset.X[^1] > angleToTravelDataset.X[0];
        angleToTravelPolynomial = Polynomial.Fit([.. angleToTravelDataset.X], [.. angleToTravelDataset.Y], 3);
    }
}