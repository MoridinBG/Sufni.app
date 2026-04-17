using System;
using Sufni.App.Models;
using Sufni.App.Models.SensorConfigurations;

namespace Sufni.App.Services;

internal static class RearTravelCalibrationBuilder
{
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

        if (!setup.TryBuildRearShockCalibration(bike, out var shockCalibration, out errorMessage))
        {
            return false;
        }

        try
        {
            var mapping = RearSuspensionMappingFactory.Build(rearSuspension);
            calibration = new RearTravelCalibration(
                mapping.MaxWheelTravel,
                measurement => mapping.ShockStrokeToWheelTravel(shockCalibration!.MeasurementToShockStroke(measurement)));
            return true;
        }
        catch (Exception exception)
        {
            errorMessage = exception.Message;
            return false;
        }
    }
}