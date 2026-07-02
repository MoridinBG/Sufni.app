using System;
using System.Linq;
using MathNet.Numerics;
using Sufni.Kinematics;

using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Models.SensorConfigurations;
namespace Sufni.App.Bikes.Services;

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
        LinkageSpec? linkageSpec = null;
        switch (bike.RearSuspension)
        {
            case RearSuspensionSpec.Hardtail:
                return true;
            case RearSuspensionSpec.Linkage linkage:
                linkageSpec = linkage.Spec;
                rearSuspension = new LinkageRearSuspension(Linkage.FromSpec(linkage.Spec));
                break;
            case RearSuspensionSpec.LeverageRatio leverageRatio:
                rearSuspension = new LeverageRatioRearSuspension(leverageRatio.Spec);
                break;
            case RearSuspensionSpec.LinkageDraft:
            case RearSuspensionSpec.LeverageRatioDraft:
                errorMessage = "Rear suspension is incomplete.";
                return false;
            default:
                errorMessage = "Unknown rear suspension.";
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
                LinearShockSensorConfiguration linearShock when IsCompatibleLinearShock(linearShock.Type, bike.RearSuspension.Kind) =>
                    BuildLinearCalibration(linearShock, bike, rearSuspension),
                RotationalShockSensorConfiguration rotationalShock when linkageSpec is not null =>
                    BuildRotationalCalibration(rotationalShock, linkageSpec, rearSuspension),
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
        var maxShockStroke = rearSuspension switch
        {
            LeverageRatioRearSuspension leverageRatioRearSuspension =>
                LeverageRatioShockStrokeRules.TryValidate(
                    bike.ShockStroke,
                    leverageRatioRearSuspension.LeverageRatio,
                    out var validatedShockStroke,
                    out var errorMessage)
                    ? validatedShockStroke
                    : throw new InvalidOperationException(errorMessage),
            _ => bike.ShockStroke ?? configuration.Length,
        };

        return BuildTravelCalibration(rearSuspension, maxShockStroke, measurement => measurement * measurementToStroke, measurementWraps: false);
    }

    private static RearTravelCalibration BuildRotationalCalibration(
        RotationalShockSensorConfiguration configuration,
        LinkageSpec linkage,
        RearSuspension rearSuspension)
    {
        var central = linkage.Joints.FirstOrDefault(joint => joint.Name == configuration.CentralJoint);
        var adjacent1 = linkage.Joints.FirstOrDefault(joint => joint.Name == configuration.AdjacentJoint1);
        var adjacent2 = linkage.Joints.FirstOrDefault(joint => joint.Name == configuration.AdjacentJoint2);
        if (central is null || adjacent1 is null || adjacent2 is null)
        {
            throw new InvalidOperationException("Rotational shock sensor joints could not be resolved from the linkage.");
        }

        var startAngle = GeometryUtils.CalculateAngleAtPoint(
            central.X,
            central.Y,
            adjacent1.X,
            adjacent1.Y,
            adjacent2.X,
            adjacent2.Y);
        var solution = new KinematicSolver(linkage).SolveSuspensionMotion();
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
            },
            measurementWraps: true);
    }

    private static RearTravelCalibration BuildTravelCalibration(
        RearSuspension rearSuspension,
        double maxShockStroke,
        Func<ushort, double> measurementToShockStroke,
        bool measurementWraps)
    {
        return rearSuspension switch
        {
            LinkageRearSuspension linkageRearSuspension => BuildLinkageTravelCalibration(linkageRearSuspension.Linkage, maxShockStroke, measurementToShockStroke, measurementWraps),
            LeverageRatioRearSuspension leverageRatioRearSuspension => new RearTravelCalibration(
                leverageRatioRearSuspension.LeverageRatio.WheelTravelAt(maxShockStroke),
                measurement => leverageRatioRearSuspension.LeverageRatio.WheelTravelAt(Math.Min(maxShockStroke, measurementToShockStroke(measurement))),
                measurementWraps),
            _ => throw new ArgumentOutOfRangeException(nameof(rearSuspension)),
        };
    }

    private static RearTravelCalibration BuildLinkageTravelCalibration(
        Linkage linkage,
        double maxShockStroke,
        Func<ushort, double> measurementToShockStroke,
        bool measurementWraps)
    {
        var solution = new KinematicSolver(linkage.ToSpec()).SolveSuspensionMotion();
        var dataset = new BikeCharacteristics(solution).ShockStrokeToWheelTravelDataset();
        return new RearTravelCalibration(
            dataset.Y[^1],
            measurement => TravelInterpolation.WheelTravelAt(dataset, Math.Min(maxShockStroke, measurementToShockStroke(measurement))),
            measurementWraps);
    }
}
