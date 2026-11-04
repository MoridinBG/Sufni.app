using System.Collections.Generic;
using System.Linq;
using System;
using Sufni.App.LiveDaq.Services.Imu;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Coordinators.Capture;

internal sealed class LiveDisplayProjector(BikeData bikeData)
{
    private readonly LiveImuDisplaySignalProcessor imuDisplaySignalProcessor = new();

    public bool HasPitchRollData { get; private set; }

    public void Reset()
    {
        imuDisplaySignalProcessor.Reset();
        HasPitchRollData = false;
    }

    public LiveDisplayUpdate.Travel ProjectTravel(AcceptedTravelBatch batch, long epoch)
    {
        var front = new double[batch.Times.Length];
        var rear = new double[batch.Times.Length];
        for (var index = 0; index < batch.Times.Length; index++)
        {
            front[index] = batch.FrontCounts is { } frontCounts
                ? ConvertTravel(frontCounts[index], bikeData.FrontMeasurementToTravel, bikeData.FrontMaxTravel)
                : double.NaN;
            rear[index] = batch.RearCounts is { } rearCounts
                ? ConvertTravel(rearCounts[index], bikeData.RearMeasurementToTravel, bikeData.RearMaxTravel)
                : double.NaN;
        }

        return new LiveDisplayUpdate.Travel(epoch, batch.Times, front, rear);
    }

    public LiveDisplayUpdate.Imu? ProjectImu(
        IReadOnlyList<AcceptedImuSeries> series, LiveSessionHeader header, long epoch)
    {
        var inputs = series.Select(input => new LiveImuDisplayInputSeries(
            input.Location, input.Times, input.Records,
            header.ImuCalibrationScales.GetAccelScale(input.Location),
            header.ImuCalibrationScales.GetGyroScale(input.Location))).ToArray();
        var display = imuDisplaySignalProcessor.ProcessBatch(inputs, header.AcceptedImuHz);
        HasPitchRollData |= display.FramePitchRoll is not null;
        return display.VibrationTimes.Count == 0 && display.FramePitchRoll is null
            ? null
            : new LiveDisplayUpdate.Imu(epoch, display.VibrationTimes, display.VibrationRms, display.FramePitchRoll);
    }

    private static double ConvertTravel(ushort measurement, Func<ushort, double>? measurementToTravel, double? maxTravel)
    {
        if (measurementToTravel is null)
        {
            return double.NaN;
        }

        var travel = measurementToTravel(measurement);
        if (!double.IsFinite(travel))
        {
            return double.NaN;
        }

        return maxTravel is double finiteMaxTravel
            ? Math.Clamp(travel, 0, finiteMaxTravel)
            : travel;
    }
}
