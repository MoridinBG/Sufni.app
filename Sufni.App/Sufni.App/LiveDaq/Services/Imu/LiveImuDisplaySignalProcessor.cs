using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.Telemetry;
using Sufni.App.Sessions.Plots;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.LiveDaq.Services.Imu;

public sealed record LiveImuDisplayInputSeries(
    LiveImuLocation Location,
    IReadOnlyList<double> Times,
    IReadOnlyList<ImuRecord> Records,
    double AccelLsbPerG,
    double GyroLsbPerDps);

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

            var result = state.ProcessBatch(input.Times, input.Records);
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
        private double pitchDegrees;
        private double rollDegrees;
        private bool hasPreviousTime;
        private bool hasGravity;
        private bool hasAttitude;
        private bool pitchRollAvailable;

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

        public LocationProcessResult ProcessBatch(IReadOnlyList<double> times, IReadOnlyList<ImuRecord> records)
        {
            var count = Math.Min(times.Count, records.Count);
            if (count == 0)
            {
                return LocationProcessResult.Empty;
            }

            var samples = new List<TimedImuSample>(count);
            for (var i = 0; i < count; i++)
            {
                samples.Add(new TimedImuSample(times[i], records[i]));
            }

            return ProcessCalibratedSamples(samples);
        }

        private LocationProcessResult ProcessCalibratedSamples(IReadOnlyList<TimedImuSample> samples)
        {
            if (samples.Count == 0)
            {
                return LocationProcessResult.Empty;
            }

            var times = new double[samples.Count];
            var rms = new double[samples.Count];
            var pitch = pitchRollAvailable ? new double[samples.Count] : [];
            var roll = pitchRollAvailable ? new double[samples.Count] : [];

            for (var i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                var dtSeconds = ImuDisplaySignalProcessor.CalculateDeltaSeconds(sample.Time, ref previousTime, ref hasPreviousTime);
                var accel = ImuDisplaySignalProcessor.ConvertAccel(sample.Record, accelLsbPerG);
                gravity = hasGravity
                    ? ImuDisplaySignalProcessor.UpdateGravity(gravity, accel, dtSeconds)
                    : ImuDisplaySignalProcessor.EstimateInitialGravity(accel);
                hasGravity = true;
                var dynamic = accel - gravity;

                times[i] = sample.Time;
                rms[i] = rmsState.Add(dynamic.Magnitude);

                if (!pitchRollAvailable)
                {
                    continue;
                }

                ImuDisplaySignalProcessor.UpdateAttitude(
                    accel,
                    sample.Record,
                    gyroLsbPerDps,
                    dtSeconds,
                    correctionWeight: 1.0,
                    ref hasAttitude,
                    ref pitchDegrees,
                    ref rollDegrees);

                pitch[i] = pitchDegrees;
                roll[i] = rollDegrees;
            }

            return new LocationProcessResult(
                times,
                rms,
                pitchRollAvailable ? new FramePitchRollSeries(times.ToArray(), pitch, roll) : null);
        }
    }
}

