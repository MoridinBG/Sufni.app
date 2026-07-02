using Sufni.Telemetry;

using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.Stores;
namespace Sufni.App.Bikes.Services;

internal static class TelemetryBikeData
{
    public static BikeData Create(
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

    public static BikeData Create(SetupSnapshot setup, BikeSnapshot bike)
    {
        var frontSensorConfiguration = setup.FrontSensorConfigurationJson is null
            ? null
            : SensorConfiguration.FromJson(setup.FrontSensorConfigurationJson, bike);
        RearTravelCalibrationBuilder.TryBuild(setup, bike, out var rearTravelCalibration, out _);

        return Create(frontSensorConfiguration, rearTravelCalibration);
    }
}
