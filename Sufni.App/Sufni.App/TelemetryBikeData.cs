using Sufni.App.Models;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Services;
using Sufni.Telemetry;

namespace Sufni.App;

public static class TelemetryBikeData
{
    internal static BikeData Create(
        Bike bike,
        ISensorConfiguration? frontSensorConfiguration,
        RearTravelCalibration? rearTravelCalibration)
    {
        return new BikeData(
            bike.HeadAngle,
            frontSensorConfiguration?.MaxTravel,
            rearTravelCalibration?.MaxTravel,
            frontSensorConfiguration?.MeasurementToTravel,
            rearTravelCalibration?.MeasurementToTravel,
            frontSensorConfiguration?.MeasurementWraps ?? false,
            rearTravelCalibration?.MeasurementWraps ?? false);
    }

    public static BikeData Create(Setup setup, Bike bike)
    {
        var frontSensorConfiguration = setup.FrontSensorConfiguration(bike);
        RearTravelCalibrationBuilder.TryBuild(setup, bike, out var rearTravelCalibration, out _);

        return Create(bike, frontSensorConfiguration, rearTravelCalibration);
    }
}
