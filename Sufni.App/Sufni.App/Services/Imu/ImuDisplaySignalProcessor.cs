using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.App.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.Services.Imu;

public sealed record ImuVibrationSeries(byte LocationId, double[] Times, double[] RmsG);

public sealed record FramePitchRollSeries(double[] Times, double[] PitchDegrees, double[] RollDegrees);

public sealed record RecordedImuDisplaySeries(
    IReadOnlyList<ImuVibrationSeries> VibrationSeries,
    FramePitchRollSeries? FramePitchRoll);

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

public static class ImuDisplaySignalProcessor
{
    public const int GravityLowPassTimeConstantMilliseconds = 750;
    public const int VibrationRmsWindowMilliseconds = 200;
    public const int AttitudeCorrectionTimeConstantMilliseconds = 500;

    private const double MillisecondsPerSecond = 1000.0;
    private const double MinimumGravityMagnitude = 1e-6;
    private const double AttitudeAccelMagnitudeMinimumG = 0.85;
    private const double AttitudeAccelMagnitudeMaximumG = 1.15;

    public static RecordedImuDisplaySeries ProcessRecorded(TelemetryData telemetryData)
    {
        return telemetryData.ImuData is { } imuData
            ? ProcessRecorded(imuData, AttitudeCorrectionContext.CreateRecorded(telemetryData))
            : new RecordedImuDisplaySeries([], null);
    }

    public static RecordedImuDisplaySeries ProcessRecorded(RawImuData imuData)
    {
        return ProcessRecorded(imuData, AttitudeCorrectionContext.Empty);
    }

    private static RecordedImuDisplaySeries ProcessRecorded(
        RawImuData imuData,
        AttitudeCorrectionContext attitudeCorrectionContext)
    {
        if (imuData.SampleRate <= 0 || imuData.Records.Count == 0 || imuData.ActiveLocations.Count == 0)
        {
            return new RecordedImuDisplaySeries([], null);
        }

        var metaByLocation = BuildMetaLookup(imuData.Meta);
        var samplesByLocation = DeinterleaveSamples(imuData);
        var vibrationSeries = new List<ImuVibrationSeries>(samplesByLocation.Count);
        FramePitchRollSeries? framePitchRoll = null;

        foreach (var entry in samplesByLocation.OrderBy(entry => entry.Key))
        {
            if (!metaByLocation.TryGetValue(entry.Key, out var meta) || meta.AccelLsbPerG <= 0)
            {
                continue;
            }

            var result = ProcessLocation(
                entry.Value,
                imuData.SampleRate,
                meta.AccelLsbPerG,
                meta.GyroLsbPerDps,
                includePitchRoll: entry.Key == 0,
                attitudeCorrectionContext);

            if (result.RmsG.Length > 0)
            {
                vibrationSeries.Add(new ImuVibrationSeries(entry.Key, result.Times, result.RmsG));
            }

            if (entry.Key == 0)
            {
                framePitchRoll = result.FramePitchRoll;
            }
        }

        return new RecordedImuDisplaySeries(vibrationSeries, framePitchRoll);
    }

    internal static int CalculateRmsWindowSamples(double sampleRateHz)
    {
        if (!double.IsFinite(sampleRateHz) || sampleRateHz <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Round(sampleRateHz * VibrationRmsWindowMilliseconds / MillisecondsPerSecond));
    }

    internal static double LowPassAlpha(double dtSeconds, double timeConstantMilliseconds)
    {
        if (!double.IsFinite(dtSeconds) || dtSeconds <= 0)
        {
            return 0;
        }

        var tauSeconds = timeConstantMilliseconds / MillisecondsPerSecond;
        return dtSeconds / (tauSeconds + dtSeconds);
    }

    internal static AccelVector ConvertAccel(ImuRecord record, double accelLsbPerG)
    {
        return new AccelVector(
            record.Ax / accelLsbPerG,
            record.Ay / accelLsbPerG,
            record.Az / accelLsbPerG);
    }

    internal static AccelVector UpdateGravity(AccelVector gravity, AccelVector accel, double dtSeconds)
    {
        var alpha = LowPassAlpha(dtSeconds, GravityLowPassTimeConstantMilliseconds);
        return new AccelVector(
            gravity.X + alpha * (accel.X - gravity.X),
            gravity.Y + alpha * (accel.Y - gravity.Y),
            gravity.Z + alpha * (accel.Z - gravity.Z));
    }

    internal static AttitudeAngles CalculateAttitude(AccelVector gravity)
    {
        var pitch = Math.Atan2(gravity.X, gravity.Z) * 180.0 / Math.PI;
        var roll = Math.Atan2(gravity.Y, gravity.Z) * 180.0 / Math.PI;
        return new AttitudeAngles(pitch, roll);
    }

    internal static AccelVector EstimateInitialGravity(AccelVector accel)
    {
        var magnitude = accel.Magnitude;
        if (!double.IsFinite(magnitude) || magnitude <= MinimumGravityMagnitude)
        {
            return AccelVector.BikeLevelGravity;
        }

        return new AccelVector(
            accel.X / magnitude,
            accel.Y / magnitude,
            accel.Z / magnitude);
    }

    internal static bool IsAttitudeAccelerationReliable(AccelVector accel)
    {
        var magnitude = accel.Magnitude;
        return double.IsFinite(magnitude) &&
            magnitude >= AttitudeAccelMagnitudeMinimumG &&
            magnitude <= AttitudeAccelMagnitudeMaximumG;
    }

    internal static double CalculateAttitudeCorrection(
        AccelVector accel,
        double dtSeconds,
        double contextWeight)
    {
        if (contextWeight <= 0 || !IsAttitudeAccelerationReliable(accel))
        {
            return 0;
        }

        return LowPassAlpha(dtSeconds, AttitudeCorrectionTimeConstantMilliseconds) * contextWeight;
    }

    internal static void UpdateAttitude(
        AccelVector accel,
        ImuRecord record,
        double gyroLsbPerDps,
        double dtSeconds,
        double correctionWeight,
        ref bool hasAttitude,
        ref double pitchDegrees,
        ref double rollDegrees)
    {
        if (!hasAttitude)
        {
            var initialAttitude = correctionWeight > 0 && IsAttitudeAccelerationReliable(accel)
                ? CalculateAttitude(accel)
                : new AttitudeAngles(0, 0);
            pitchDegrees = initialAttitude.PitchDegrees;
            rollDegrees = initialAttitude.RollDegrees;
            hasAttitude = true;
            return;
        }

        var gyroX = record.Gx / gyroLsbPerDps;
        var gyroY = record.Gy / gyroLsbPerDps;
        var predictedRoll = rollDegrees + gyroX * dtSeconds;
        var predictedPitch = pitchDegrees + gyroY * dtSeconds;
        var correction = CalculateAttitudeCorrection(accel, dtSeconds, correctionWeight);
        if (correction <= 0)
        {
            rollDegrees = predictedRoll;
            pitchDegrees = predictedPitch;
            return;
        }

        var attitude = CalculateAttitude(accel);
        rollDegrees = predictedRoll + correction * (attitude.RollDegrees - predictedRoll);
        pitchDegrees = predictedPitch + correction * (attitude.PitchDegrees - predictedPitch);
    }

    internal static LocationProcessResult ProcessLocation(
        IReadOnlyList<TimedImuSample> samples,
        double sampleRateHz,
        double accelLsbPerG,
        double gyroLsbPerDps,
        bool includePitchRoll,
        AttitudeCorrectionContext attitudeCorrectionContext)
    {
        if (samples.Count == 0 || accelLsbPerG <= 0)
        {
            return LocationProcessResult.Empty;
        }

        var pitchRollAvailable = includePitchRoll && gyroLsbPerDps > 0;
        var rmsWindow = CalculateRmsWindowSamples(sampleRateHz);
        var rmsState = new RollingRmsState(rmsWindow);
        var times = new double[samples.Count];
        var rms = new double[samples.Count];
        var pitch = pitchRollAvailable ? new double[samples.Count] : [];
        var roll = pitchRollAvailable ? new double[samples.Count] : [];
        var gravity = AccelVector.BikeLevelGravity;
        var hasGravity = false;
        var hasPreviousTime = false;
        var previousTime = 0.0;
        var pitchDegrees = 0.0;
        var rollDegrees = 0.0;
        var hasAttitude = false;

        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            var dtSeconds = CalculateDeltaSeconds(sample.Time, ref previousTime, ref hasPreviousTime);
            var accel = ConvertAccel(sample.Record, accelLsbPerG);

            gravity = hasGravity
                ? UpdateGravity(gravity, accel, dtSeconds)
                : EstimateInitialGravity(accel);
            hasGravity = true;
            var dynamic = accel - gravity;
            var dynamicMagnitude = dynamic.Magnitude;

            times[i] = sample.Time;
            rms[i] = rmsState.Add(dynamicMagnitude);

            if (!pitchRollAvailable)
            {
                continue;
            }

            UpdateAttitude(
                accel,
                sample.Record,
                gyroLsbPerDps,
                dtSeconds,
                attitudeCorrectionContext.CorrectionWeightAt(sample.Time),
                ref hasAttitude,
                ref pitchDegrees,
                ref rollDegrees);

            pitch[i] = pitchDegrees;
            roll[i] = rollDegrees;
        }

        var pitchRoll = pitchRollAvailable
            ? new FramePitchRollSeries(times.ToArray(), pitch, roll)
            : null;

        return new LocationProcessResult(times, rms, pitchRoll);
    }

    internal static double CalculateDeltaSeconds(double currentTime, ref double previousTime, ref bool hasPreviousTime)
    {
        if (!hasPreviousTime)
        {
            previousTime = currentTime;
            hasPreviousTime = true;
            return 0;
        }

        var dtSeconds = currentTime - previousTime;
        previousTime = currentTime;
        return dtSeconds > 0 ? dtSeconds : 0;
    }

    private static Dictionary<byte, ImuMetaEntry> BuildMetaLookup(IEnumerable<ImuMetaEntry> metaEntries)
    {
        var lookup = new Dictionary<byte, ImuMetaEntry>();
        foreach (var meta in metaEntries)
        {
            lookup.TryAdd(meta.LocationId, meta);
        }

        return lookup;
    }

    private static Dictionary<byte, List<TimedImuSample>> DeinterleaveSamples(RawImuData imuData)
    {
        var samplesByLocation = new Dictionary<byte, List<TimedImuSample>>();
        foreach (var location in imuData.ActiveLocations)
        {
            samplesByLocation.TryAdd(location, []);
        }

        var locationCount = imuData.ActiveLocations.Count;
        for (var recordIndex = 0; recordIndex < imuData.Records.Count; recordIndex++)
        {
            var location = imuData.ActiveLocations[recordIndex % locationCount];
            if (!samplesByLocation.TryGetValue(location, out var samples))
            {
                samples = [];
                samplesByLocation[location] = samples;
            }

            var time = samples.Count / (double)imuData.SampleRate;
            samples.Add(new TimedImuSample(time, imuData.Records[recordIndex]));
        }

        return samplesByLocation;
    }
}

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

internal sealed class AttitudeCorrectionContext
{
    public static AttitudeCorrectionContext Empty { get; } = new();

    private const double AirtimePaddingSeconds = 0.10;
    private const double SuspensionVelocityThresholdMmPerSecond = 300.0;

    private readonly int travelSampleRate;
    private readonly Suspension? front;
    private readonly Suspension? rear;
    private readonly IReadOnlyList<Airtime> airtimes;

    private AttitudeCorrectionContext()
    {
        airtimes = [];
    }

    private AttitudeCorrectionContext(TelemetryData telemetryData)
    {
        travelSampleRate = telemetryData.Metadata?.SampleRate ?? 0;
        front = telemetryData.Front;
        rear = telemetryData.Rear;
        airtimes = telemetryData.Airtimes ?? [];
    }

    public static AttitudeCorrectionContext CreateRecorded(TelemetryData telemetryData)
    {
        return new AttitudeCorrectionContext(telemetryData);
    }

    public double CorrectionWeightAt(double timeSeconds)
    {
        if (IsWithinAirtimeWindow(timeSeconds) ||
            HasHighSuspensionVelocity(front, timeSeconds) ||
            HasHighSuspensionVelocity(rear, timeSeconds))
        {
            return 0;
        }

        return 1;
    }

    private bool IsWithinAirtimeWindow(double timeSeconds)
    {
        foreach (var airtime in airtimes)
        {
            if (timeSeconds >= airtime.Start - AirtimePaddingSeconds &&
                timeSeconds <= airtime.End + AirtimePaddingSeconds)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasHighSuspensionVelocity(Suspension? suspension, double timeSeconds)
    {
        if (travelSampleRate <= 0 || suspension is not { Present: true } || suspension.Velocity is not { Length: > 0 } velocity)
        {
            return false;
        }

        var index = (int)Math.Round(timeSeconds * travelSampleRate);
        if (index < 0 || index >= velocity.Length)
        {
            return false;
        }

        return Math.Abs(velocity[index]) >= SuspensionVelocityThresholdMmPerSecond;
    }
}

internal sealed class RollingRmsState
{
    private readonly int windowSamples;
    private readonly Queue<double> squares = new();
    private double sumSquares;

    public RollingRmsState(int windowSamples)
    {
        this.windowSamples = Math.Max(1, windowSamples);
    }

    public double Add(double value)
    {
        var square = value * value;
        squares.Enqueue(square);
        sumSquares += square;

        while (squares.Count > windowSamples)
        {
            sumSquares -= squares.Dequeue();
        }

        return Math.Sqrt(sumSquares / squares.Count);
    }
}

internal readonly record struct TimedImuSample(double Time, ImuRecord Record);

internal readonly record struct AccelVector(double X, double Y, double Z)
{
    public static AccelVector BikeLevelGravity { get; } = new(0, 0, 1);

    public double Magnitude => Math.Sqrt(X * X + Y * Y + Z * Z);

    public static AccelVector operator -(AccelVector left, AccelVector right)
    {
        return new AccelVector(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }
}

internal readonly record struct AttitudeAngles(double PitchDegrees, double RollDegrees);

internal sealed record LocationProcessResult(double[] Times, double[] RmsG, FramePitchRollSeries? FramePitchRoll)
{
    public static readonly LocationProcessResult Empty = new([], [], null);
}
