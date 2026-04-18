using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics;
using Sufni.App.Models;
using Sufni.App.Models.SensorConfigurations;
using Sufni.Kinematics;

namespace Sufni.App.Services;

internal static class RearTravelCalibrationBuilder
{
    private const double MeasurementToAngle = 2.0 * Math.PI / 4096;

    public static bool TryBuild(
        Setup setup,
        Bike bike,
        out RearTravelCalibration? calibration,
        out string? errorMessage)
    {
        calibration = null;
        errorMessage = null;

        RearSuspension rearSuspension;
        switch (RearSuspensionResolver.Resolve(bike.RearSuspensionKind, bike.Linkage, bike.LeverageRatio))
        {
            case RearSuspensionResolution.Hardtail:
                return true;
            case RearSuspensionResolution.Linkage linkageResolution:
                rearSuspension = linkageResolution.Value;
                break;
            case RearSuspensionResolution.LeverageRatio leverageRatioResolution:
                rearSuspension = leverageRatioResolution.Value;
                break;
            case RearSuspensionResolution.Invalid invalid:
                errorMessage = RearSuspensionResolutionMessages.ForSave(invalid.Error);
                return false;
            default:
                errorMessage = "Unknown rear suspension resolution.";
                return false;
        }

        if (setup.RearSensorConfigurationJson is null)
        {
            errorMessage = "Rear sensor configuration is missing.";
            return false;
        }

        var payload = SensorConfiguration.FromJson(setup.RearSensorConfigurationJson);
        if (payload is null)
        {
            errorMessage = "Rear sensor configuration is invalid.";
            return false;
        }

        try
        {
            calibration = payload switch
            {
                LinearShockSensorConfiguration linearShock when IsCompatibleLinearShock(linearShock.Type, bike.RearSuspensionKind) =>
                    BuildLinearCalibration(linearShock, bike, rearSuspension),
                RotationalShockSensorConfiguration rotationalShock when bike.RearSuspensionKind == RearSuspensionKind.Linkage =>
                    BuildRotationalCalibration(rotationalShock, bike, rearSuspension),
                _ => null
            };

            if (calibration is null)
            {
                errorMessage = "Rear sensor configuration is not compatible with the selected bike rear suspension.";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            errorMessage = exception.Message;
            return false;
        }
    }

    private static bool IsCompatibleLinearShock(SensorType type, RearSuspensionKind rearSuspensionKind) =>
        (type, rearSuspensionKind) switch
        {
            (SensorType.LinearShock, RearSuspensionKind.Linkage) => true,
            (SensorType.LinearShockStroke, RearSuspensionKind.LeverageRatio) => true,
            _ => false,
        };

    private static RearTravelCalibration BuildLinearCalibration(
        LinearShockSensorConfiguration configuration,
        Bike bike,
        RearSuspension rearSuspension)
    {
        var measurementToStroke = LinearSensorCalibrationMath.MeasurementToStroke(configuration.Length, configuration.Resolution);
        var maxShockStroke = bike.ShockStroke ?? configuration.Length;
        return BuildTravelCalibration(rearSuspension, maxShockStroke, measurement => measurement * measurementToStroke);
    }

    private static RearTravelCalibration BuildRotationalCalibration(
        RotationalShockSensorConfiguration configuration,
        Bike bike,
        RearSuspension rearSuspension)
    {
        if (bike.Linkage is null)
        {
            throw new InvalidOperationException("Rear linkage is required for a rotational shock sensor.");
        }

        var central = bike.Linkage.Joints.FirstOrDefault(joint => joint.Name == configuration.CentralJoint);
        var adjacent1 = bike.Linkage.Joints.FirstOrDefault(joint => joint.Name == configuration.AdjacentJoint1);
        var adjacent2 = bike.Linkage.Joints.FirstOrDefault(joint => joint.Name == configuration.AdjacentJoint2);
        if (central is null || adjacent1 is null || adjacent2 is null)
        {
            throw new InvalidOperationException("Rotational shock sensor joints could not be resolved from the linkage.");
        }

        var startAngle = GeometryUtils.CalculateAngleAtPoint(central, adjacent1, adjacent2);
        var solution = new KinematicSolver(bike.Linkage).SolveSuspensionMotion();
        var dataset = new BikeCharacteristics(solution)
            .AngleToShockStrokeDataset(configuration.CentralJoint, configuration.AdjacentJoint1, configuration.AdjacentJoint2);
        var anglesIncreasing = dataset.X[^1] > dataset.X[0];
        var polynomial = Polynomial.Fit([.. dataset.X], [.. dataset.Y], 3);

        return BuildTravelCalibration(
            rearSuspension,
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
    }

    private static RearTravelCalibration BuildTravelCalibration(
        RearSuspension rearSuspension,
        double maxShockStroke,
        Func<ushort, double> measurementToShockStroke)
    {
        return rearSuspension switch
        {
            LinkageRearSuspension linkageRearSuspension => BuildLinkageTravelCalibration(linkageRearSuspension.Linkage, maxShockStroke, measurementToShockStroke),
            LeverageRatioRearSuspension leverageRatioRearSuspension => new RearTravelCalibration(
                leverageRatioRearSuspension.LeverageRatio.MaxWheelTravel,
                measurement => leverageRatioRearSuspension.LeverageRatio.WheelTravelAt(Math.Min(maxShockStroke, measurementToShockStroke(measurement)))),
            _ => throw new ArgumentOutOfRangeException(nameof(rearSuspension)),
        };
    }

    private static RearTravelCalibration BuildLinkageTravelCalibration(
        Linkage linkage,
        double maxShockStroke,
        Func<ushort, double> measurementToShockStroke)
    {
        var solution = new KinematicSolver(linkage).SolveSuspensionMotion();
        var dataset = new BikeCharacteristics(solution).ShockStrokeToWheelTravelDataset();
        return new RearTravelCalibration(
            dataset.Y[^1],
            measurement => Interpolate(dataset.X, dataset.Y, Math.Min(maxShockStroke, measurementToShockStroke(measurement))));
    }

    private static double Interpolate(IReadOnlyList<double> x, IReadOnlyList<double> y, double value)
    {
        if (value <= x[0])
        {
            return y[0];
        }

        if (value >= x[^1])
        {
            return y[^1];
        }

        for (var index = 1; index < x.Count; index++)
        {
            if (value > x[index])
            {
                continue;
            }

            var progress = (value - x[index - 1]) / (x[index] - x[index - 1]);
            return y[index - 1] + (y[index] - y[index - 1]) * progress;
        }

        return y[^1];
    }
}