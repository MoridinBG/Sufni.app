using Sufni.Telemetry;

using Sufni.App.Setups.Models.SensorConfigurations;
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

}
