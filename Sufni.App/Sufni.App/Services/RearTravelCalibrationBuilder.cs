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

        if (!bike.TryResolveRearSuspension(out var rearSuspension, out errorMessage))
        {
            return false;
        }

        if (rearSuspension is null)
        {
            return true;
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