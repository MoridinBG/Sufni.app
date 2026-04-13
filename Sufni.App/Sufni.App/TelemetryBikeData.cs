using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App;

public static class TelemetryBikeData
{
    public static BikeData Create(Setup setup, Bike bike)
    {
        var frontSensorConfiguration = setup.FrontSensorConfiguration(bike);
        var rearSensorConfiguration = setup.RearSensorConfiguration(bike);

        return new BikeData(
            bike.HeadAngle,
            frontSensorConfiguration?.MaxTravel,
            rearSensorConfiguration?.MaxTravel,
            frontSensorConfiguration?.MeasurementToTravel,
            rearSensorConfiguration?.MeasurementToTravel);
    }
}