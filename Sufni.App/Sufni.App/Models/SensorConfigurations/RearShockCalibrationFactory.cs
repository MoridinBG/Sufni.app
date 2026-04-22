using System;
using System.Linq;
using MathNet.Numerics;
using Sufni.App.Models;
using Sufni.Kinematics;

namespace Sufni.App.Models.SensorConfigurations;

internal static class RearShockCalibrationFactory
{
    private const double MeasurementToAngle = 2.0 * Math.PI / 4096;

    public static bool TryBuild(
        string rearSensorConfigurationJson,
        Bike bike,
        out RearShockCalibration? calibration,
        out string? errorMessage)
    {
        calibration = null;
        errorMessage = null;

        var payload = SensorConfiguration.FromJson(rearSensorConfigurationJson);
        if (payload is null)
        {
            errorMessage = "Rear sensor configuration is invalid.";
            return false;
        }

        switch (payload)
        {
            case LinearShockSensorConfiguration linearShock when bike.RearSuspensionKind == RearSuspensionKind.Linkage:
                calibration = BuildLinearCalibration(linearShock.Length, linearShock.Resolution, bike.ShockStroke ?? linearShock.Length);
                return true;

            case LinearShockStrokeSensorConfiguration linearShockStroke when bike.RearSuspensionKind == RearSuspensionKind.LeverageRatio:
                calibration = BuildLinearCalibration(linearShockStroke.Length, linearShockStroke.Resolution, bike.ShockStroke ?? linearShockStroke.Length);
                return true;

            case RotationalShockSensorConfiguration rotationalShock when bike.RearSuspensionKind == RearSuspensionKind.Linkage:
                return TryBuildRotationalCalibration(rotationalShock, bike, out calibration, out errorMessage);

            default:
                errorMessage = "Rear sensor configuration is not compatible with the selected bike rear suspension.";
                return false;
        }
    }

    private static RearShockCalibration BuildLinearCalibration(double length, int resolution, double maxShockStroke)
    {
        var measurementToStroke = length / (2 ^ resolution);
        return new RearShockCalibration(
            maxShockStroke,
            measurement => measurement * measurementToStroke);
    }

    private static bool TryBuildRotationalCalibration(
        RotationalShockSensorConfiguration configuration,
        Bike bike,
        out RearShockCalibration? calibration,
        out string? errorMessage)
    {
        calibration = null;
        errorMessage = null;

        if (bike.Linkage is null)
        {
            errorMessage = "Rear linkage is required for a rotational shock sensor.";
            return false;
        }

        var central = bike.Linkage.Joints.FirstOrDefault(joint => joint.Name == configuration.CentralJoint);
        var adjacent1 = bike.Linkage.Joints.FirstOrDefault(joint => joint.Name == configuration.AdjacentJoint1);
        var adjacent2 = bike.Linkage.Joints.FirstOrDefault(joint => joint.Name == configuration.AdjacentJoint2);
        if (central is null || adjacent1 is null || adjacent2 is null)
        {
            errorMessage = "Rotational shock sensor joints could not be resolved from the linkage.";
            return false;
        }

        var startAngle = GeometryUtils.CalculateAngleAtPoint(central, adjacent1, adjacent2);
        var solution = new KinematicSolver(bike.Linkage).SolveSuspensionMotion();
        var dataset = new BikeCharacteristics(solution, frontStroke: bike.ForkStroke, headAngle: bike.HeadAngle)
            .AngleToShockStrokeDataset(configuration.CentralJoint, configuration.AdjacentJoint1, configuration.AdjacentJoint2);
        var anglesIncreasing = dataset.X[^1] > dataset.X[0];
        var polynomial = Polynomial.Fit([.. dataset.X], [.. dataset.Y], 3);

        calibration = new RearShockCalibration(
            dataset.Y[^1],
            measurement =>
            {
                var measuredAngle = measurement * MeasurementToAngle;
                if (!anglesIncreasing)
                {
                    measuredAngle = -measuredAngle;
                }

                return polynomial.Evaluate(startAngle + measuredAngle);
            });
        return true;
    }
}