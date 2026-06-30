using Sufni.Telemetry;

using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Models.SensorConfigurations;
namespace Sufni.App.Bikes.Services;

public static class TelemetryBikeData
{
    internal static BikeData Create(
        Bike bike,
        ISensorConfiguration? frontSensorConfiguration,
        RearTravelCalibration? rearTravelCalibration)
    {
        return new BikeData(
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
