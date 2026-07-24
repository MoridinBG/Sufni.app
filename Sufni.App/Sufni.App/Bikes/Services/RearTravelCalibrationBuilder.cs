using System;
using System.Linq;
using MathNet.Numerics;
using Sufni.Kinematics;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.Stores;
namespace Sufni.App.Bikes.Services;

internal interface IRearTravelCalibrationBuilder
{
    RearTravelCalibrationBuildResult TryBuild(SetupSnapshot setup, BikeSnapshot bike);
}

internal sealed record RearTravelCalibrationBuildResult(
    bool Succeeded,
    RearTravelCalibration? Calibration,
    string? ErrorMessage);

internal sealed class RearTravelCalibrationBuilder(IKinematicSolutionCache kinematicSolutionCache) : IRearTravelCalibrationBuilder
{
    private const double MeasurementToAngle = 2.0 * Math.PI / 4096;
    private const int MaximumLookupLeveragePointCount = 1601;

    public RearTravelCalibrationBuildResult TryBuild(SetupSnapshot setup, BikeSnapshot bike)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(bike);

        var rearSuspension = bike.RearSuspension;
        if (rearSuspension is RearSuspensionSpec.Hardtail)
        {
            return Success(null);
        }

        if (rearSuspension is RearSuspensionSpec.LinkageDraft or RearSuspensionSpec.LeverageRatioDraft)
        {
            return Failure("Rear suspension is incomplete.");
        }

        if (rearSuspension is not (RearSuspensionSpec.Linkage or RearSuspensionSpec.LeverageRatio))
        {
            return Failure("Unknown rear suspension.");
        }

        if (setup.RearSensorConfigurationJson is null)
        {
            return Failure("Rear sensor configuration is missing.");
        }

        var payload = SensorConfiguration.FromJson(setup.RearSensorConfigurationJson);
        if (payload is null)
        {
            return Failure("Rear sensor configuration is invalid.");
        }

        try
        {
            var calibration = payload switch
            {
                LinearShockSensorConfiguration linearShock when IsCompatibleLinearShock(linearShock.Type, rearSuspension) =>
                    BuildLinearCalibration(
                        linearShock,
                        bike.ShockStroke,
                        rearSuspension,
                        rearSuspension is RearSuspensionSpec.Linkage linkage
                            ? CreateLinkageCharacteristics(linkage.Spec)
                            : null),
                RotationalShockSensorConfiguration rotationalShock when rearSuspension is RearSuspensionSpec.Linkage linkage =>
                    BuildRotationalCalibration(
                        rotationalShock,
                        linkage.Spec,
                        CreateLinkageCharacteristics(linkage.Spec)),
                _ => null
            };

            if (calibration is null)
            {
                return Failure("Rear sensor configuration is not compatible with the selected bike rear suspension.");
            }

            return Success(calibration);
        }
        catch (Exception exception)
        {
            return Failure(exception.Message);
        }
    }

    private static bool IsCompatibleLinearShock(SensorType type, RearSuspensionSpec rearSuspension) =>
        (type, rearSuspension) switch
        {
            (SensorType.LinearShock, RearSuspensionSpec.Linkage) => true,
            (SensorType.LinearShockStroke, RearSuspensionSpec.LeverageRatio) => true,
            _ => false,
        };

    private static RearTravelCalibration BuildLinearCalibration(
        LinearShockSensorConfiguration configuration,
        double? shockStroke,
        RearSuspensionSpec rearSuspension,
        BikeCharacteristics? linkageCharacteristics)
    {
        var measurementToStroke = LinearSensorCalibrationMath.MeasurementToStroke(configuration.Length, configuration.Resolution);
        var maxShockStroke = rearSuspension switch
        {
            RearSuspensionSpec.LeverageRatio leverageRatio =>
                LeverageRatioShockStrokeRules.TryValidate(
                    shockStroke,
                    leverageRatio.Spec,
                    out var validatedShockStroke,
                    out var errorMessage)
                    ? validatedShockStroke
                    : throw new InvalidOperationException(errorMessage),
            RearSuspensionSpec.Linkage => shockStroke ?? configuration.Length,
            _ => throw new ArgumentOutOfRangeException(nameof(rearSuspension)),
        };

        var calibration = BuildTravelCalibration(
            rearSuspension,
            linkageCharacteristics,
            maxShockStroke,
            measurement => measurement * measurementToStroke,
            measurementWraps: false);
        return rearSuspension switch
        {
            RearSuspensionSpec.Linkage => AddLookupTable(calibration),
            RearSuspensionSpec.LeverageRatio leverageRatio
                when leverageRatio.Spec.Points.Count <= MaximumLookupLeveragePointCount =>
                    AddLookupTable(calibration),
            _ => calibration,
        };
    }

    private static RearTravelCalibration BuildRotationalCalibration(
        RotationalShockSensorConfiguration configuration,
        LinkageSpec linkage,
        BikeCharacteristics characteristics)
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
        var dataset = characteristics.AngleToShockStrokeDataset(
            configuration.CentralJoint,
            configuration.AdjacentJoint1,
            configuration.AdjacentJoint2);
        var anglesIncreasing = dataset.X[^1] > dataset.X[0];
        var polynomial = Polynomial.Fit([.. dataset.X], [.. dataset.Y], 3);

        return AddLookupTable(
            BuildLinkageTravelCalibration(
                characteristics,
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
                measurementWraps: true));
    }

    private static RearTravelCalibration BuildTravelCalibration(
        RearSuspensionSpec rearSuspension,
        BikeCharacteristics? linkageCharacteristics,
        double maxShockStroke,
        Func<ushort, double> measurementToShockStroke,
        bool measurementWraps)
    {
        return rearSuspension switch
        {
            RearSuspensionSpec.Linkage => BuildLinkageTravelCalibration(
                linkageCharacteristics ?? throw new InvalidOperationException("Linkage movement could not be calculated."),
                maxShockStroke,
                measurementToShockStroke,
                measurementWraps),
            RearSuspensionSpec.LeverageRatio leverageRatio => new RearTravelCalibration(
                leverageRatio.Spec.WheelTravelAt(maxShockStroke),
                measurement => leverageRatio.Spec.WheelTravelAt(Math.Min(maxShockStroke, measurementToShockStroke(measurement))),
                measurementWraps),
            _ => throw new ArgumentOutOfRangeException(nameof(rearSuspension)),
        };
    }

    private static RearTravelCalibration BuildLinkageTravelCalibration(
        BikeCharacteristics characteristics,
        double maxShockStroke,
        Func<ushort, double> measurementToShockStroke,
        bool measurementWraps)
    {
        var dataset = characteristics.ShockStrokeToWheelTravelDataset();
        return new RearTravelCalibration(
            dataset.Y[^1],
            measurement => TravelInterpolation.WheelTravelAt(dataset, Math.Min(maxShockStroke, measurementToShockStroke(measurement))),
            measurementWraps);
    }

    private BikeCharacteristics CreateLinkageCharacteristics(LinkageSpec linkage)
    {
        var solution = kinematicSolutionCache.GetOrSolve(linkage);
        return new BikeCharacteristics(solution);
    }

    private static RearTravelCalibration AddLookupTable(RearTravelCalibration calibration)
    {
        var table = new AdcTravelLookupTable(calibration.MeasurementToTravel);
        return calibration with { MeasurementToTravel = table.MeasurementToTravel };
    }

    private static RearTravelCalibrationBuildResult Success(RearTravelCalibration? calibration) =>
        new(true, calibration, null);

    private static RearTravelCalibrationBuildResult Failure(string? errorMessage) =>
        new(false, null, errorMessage);
}
