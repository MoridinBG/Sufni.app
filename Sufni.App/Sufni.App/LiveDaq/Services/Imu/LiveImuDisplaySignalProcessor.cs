using System;
using System.Collections.Generic;
using System.Numerics;
using Sufni.Telemetry;
using Sufni.App.Sessions.Plots;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.LiveDaq.Services.Imu;

public sealed record LiveImuDisplayInputSeries(
    LiveImuLocation Location,
    IReadOnlyList<double> Times,
    IReadOnlyList<ImuRecord> Records,
    double AccelLsbPerG,
    double GyroLsbPerDps, ulong? FirstIndex = null);

public sealed record LiveImuDisplaySeries(
    IReadOnlyDictionary<LiveImuLocation, IReadOnlyList<double>> VibrationTimes,
    IReadOnlyDictionary<LiveImuLocation, IReadOnlyList<double>> VibrationRms,
    FramePitchRollSeries? FramePitchRoll);

public sealed class LiveImuDisplaySignalProcessor
{
    private readonly Dictionary<LiveImuLocation, StreamingLocationState> locationStates = [];

    public LiveImuDisplaySeries ProcessBatch(IReadOnlyList<LiveImuDisplayInputSeries> inputSeries, double sampleRateHz)
    {
        var vibrationTimes = new Dictionary<LiveImuLocation, IReadOnlyList<double>>();
        var vibrationRms = new Dictionary<LiveImuLocation, IReadOnlyList<double>>();
        FramePitchRollSeries? framePitchRoll = null;

        foreach (var input in inputSeries)
        {
            if (input.AccelLsbPerG <= 0 || input.Times.Count == 0 || input.Records.Count == 0)
            {
                continue;
            }

            if (!locationStates.TryGetValue(input.Location, out var state) ||
                !state.Matches(input.AccelLsbPerG, input.GyroLsbPerDps, sampleRateHz))
            {
                state = new StreamingLocationState(input.Location, input.AccelLsbPerG, input.GyroLsbPerDps, sampleRateHz);
                locationStates[input.Location] = state;
            }

            var result = state.ProcessBatch(input.Times, input.Records, input.FirstIndex);
            if (result.RmsG.Length > 0)
            {
                vibrationTimes[input.Location] = result.Times;
                vibrationRms[input.Location] = result.RmsG;
            }

            if (input.Location == LiveImuLocation.Frame)
            {
                framePitchRoll = result.FramePitchRoll;
            }
        }

        return new LiveImuDisplaySeries(vibrationTimes, vibrationRms, framePitchRoll);
    }

    public void Reset()
    {
        locationStates.Clear();
    }

    private sealed class StreamingLocationState
    {
        private readonly double accelLsbPerG;
        private readonly double gyroLsbPerDps;
        private readonly double sampleRateHz;
        private readonly RollingRmsState rmsState;
        private AccelVector gravity = AccelVector.BikeLevelGravity;
        private double previousTime;
        private Quaternion? orientation;
        private ulong? nextIndex;
        private bool hasPreviousTime;
        private bool hasGravity;
        private readonly bool pitchRollAvailable;

        public StreamingLocationState(
            LiveImuLocation location,
            double accelLsbPerG,
            double gyroLsbPerDps,
            double sampleRateHz)
        {
            this.accelLsbPerG = accelLsbPerG;
            this.gyroLsbPerDps = gyroLsbPerDps;
            this.sampleRateHz = sampleRateHz;
            rmsState = new RollingRmsState(ImuDisplaySignalProcessor.CalculateRmsWindowSamples(sampleRateHz));
            pitchRollAvailable = location == LiveImuLocation.Frame && gyroLsbPerDps > 0;
        }

        public bool Matches(double accelScale, double gyroScale, double sampleRate)
        {
            return accelLsbPerG.Equals(accelScale) &&
                gyroLsbPerDps.Equals(gyroScale) &&
                sampleRateHz.Equals(sampleRate);
        }

        public LocationProcessResult ProcessBatch(IReadOnlyList<double> times, IReadOnlyList<ImuRecord> records, ulong? firstIndex)
        {
            var count = Math.Min(times.Count, records.Count);
            if (count == 0)
            {
                return LocationProcessResult.Empty;
            }

            if (firstIndex.HasValue && firstIndex != nextIndex) orientation = null;
            nextIndex = firstIndex + (ulong)count;
            var outputTimes = new double[count];
            var rms = new double[count];
            var pitch = pitchRollAvailable ? new double[count] : [];
            var roll = pitchRollAvailable ? new double[count] : [];

            for (var i = 0; i < count; i++)
            {
                var sample = records[i];
                var dtSeconds = ImuDisplaySignalProcessor.CalculateDeltaSeconds(times[i], ref previousTime, ref hasPreviousTime);
                var accel = ImuDisplaySignalProcessor.ConvertAccel(sample, accelLsbPerG);
                gravity = hasGravity
                    ? ImuDisplaySignalProcessor.UpdateGravity(gravity, accel, dtSeconds)
                    : ImuDisplaySignalProcessor.EstimateInitialGravity(accel);
                hasGravity = true;
                var dynamic = accel - gravity;

                outputTimes[i] = times[i];
                rms[i] = rmsState.Add(dynamic.Magnitude);

                if (!pitchRollAvailable)
                {
                    continue;
                }

                orientation = FrameAttitudeEstimator.Advance(orientation, sample, dtSeconds, accelLsbPerG, gyroLsbPerDps);
                var attitude = FrameAttitudeEstimator.Read(orientation.Value);
                pitch[i] = attitude.PitchDegrees;
                roll[i] = attitude.RollDegrees;
            }

            return new LocationProcessResult(
                outputTimes,
                rms,
                pitchRollAvailable ? new FramePitchRollSeries(outputTimes, pitch, roll) : null);
        }
    }
}

