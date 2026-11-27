using System;
using Sufni.Telemetry;

using Sufni.App.Setups.Models.SensorConfigurations;
namespace Sufni.App.Bikes.Services;

internal static class TelemetryBikeData
{
    private const int CircularCounts = 4096;

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
            rearTravelCalibration?.MeasurementWraps ?? false,
            frontSensorConfiguration is null ? null : MaxTravelCount(frontSensorConfiguration.MeasurementToTravel,
                frontSensorConfiguration.MaxTravel, frontSensorConfiguration.MeasurementWraps),
            rearTravelCalibration is null ? null : MaxTravelCount(rearTravelCalibration.MeasurementToTravel,
                rearTravelCalibration.MaxTravel, rearTravelCalibration.MeasurementWraps));
    }

    // Every calibration puts the top of the stroke at count 0 and grows travel with the count.
    private static ushort MaxTravelCount(Func<ushort, double> measurementToTravel, double maxTravel, bool wraps)
    {
        var last = wraps ? CircularCounts - 1 : ushort.MaxValue;
        for (var count = 0; count < last; count++)
        {
            if (measurementToTravel((ushort)count) >= maxTravel) return (ushort)count;
        }

        return (ushort)last;
    }
}
