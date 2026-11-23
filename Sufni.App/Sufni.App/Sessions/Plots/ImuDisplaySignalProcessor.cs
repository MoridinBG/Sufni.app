using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Plots;

public sealed record ImuDisplayValueSegment(double[] Times, double[] Values);

public sealed record FramePitchRollSegment(double[] Times, double[] PitchDegrees, double[] RollDegrees);

public sealed record ImuVibrationSeries(
    byte LocationId,
    double[] Times,
    double[] RmsG,
    IReadOnlyList<ImuDisplayValueSegment> Segments)
{
    public ImuVibrationSeries(byte locationId, double[] times, double[] rmsG)
        : this(locationId, times, rmsG, [])
    {
    }
}

public sealed record FramePitchRollSeries(
    double[] Times,
    double[] PitchDegrees,
    double[] RollDegrees,
    IReadOnlyList<FramePitchRollSegment> Segments)
{
    public FramePitchRollSeries(double[] times, double[] pitchDegrees, double[] rollDegrees)
        : this(times, pitchDegrees, rollDegrees, [])
    {
    }
}

public sealed record RecordedImuDisplaySeries(
    IReadOnlyList<ImuVibrationSeries> VibrationSeries,
    FramePitchRollSeries? FramePitchRoll);

public static class ImuDisplaySignalProcessor
{
    public const int GravityLowPassTimeConstantMilliseconds = 750;
    public const int VibrationRmsWindowMilliseconds = 200;

    private const double MillisecondsPerSecond = 1000.0;
    private const double MinimumGravityMagnitude = 1e-6;

    public static RecordedImuDisplaySeries ProcessRecorded(TelemetryData telemetryData, IReadOnlyList<TrackSample>? track = null)
    {
        return telemetryData.ImuData is { } imuData
            ? ProcessRecorded(imuData, track is { Count: > 0 } ? TrackMotion.FromFixes(track, telemetryData.Metadata.Duration) : null)
            : new RecordedImuDisplaySeries([], null);
    }

    public static RecordedImuDisplaySeries ProcessRecorded(RawImuData imuData) => ProcessRecorded(imuData, null);

    private static RecordedImuDisplaySeries ProcessRecorded(RawImuData imuData, TrackMotion? track)
    {
        if (imuData.SampleRate <= 0 || !imuData.HasSamples || imuData.ActiveLocations.Length == 0)
        {
            return new RecordedImuDisplaySeries([], null);
        }

        var metaByLocation = BuildMetaLookup(imuData.Meta);
        var sampleSegmentsByLocation = BuildSampleSegmentsByLocation(imuData);
        var vibrationSeries = new List<ImuVibrationSeries>(sampleSegmentsByLocation.Count);
        FramePitchRollSeries? framePitchRoll = null;

        foreach (var entry in sampleSegmentsByLocation.OrderBy(entry => entry.Key))
        {
            if (!metaByLocation.TryGetValue(entry.Key, out var meta) || meta.AccelLsbPerG <= 0)
            {
                continue;
            }

            var locationResults = entry.Value
                .Where(segment => segment.Count > 0)
                .Select(segment => ProcessLocation(
                    segment,
                    imuData.SampleRate,
                    meta,
                    includePitchRoll: entry.Key == 0,
                    track))
                .Where(result => result.RmsG.Length > 0)
                .ToArray();

            if (locationResults.Length > 0)
            {
                vibrationSeries.Add(new ImuVibrationSeries(
                    entry.Key,
                    locationResults.SelectMany(result => result.Times).ToArray(),
                    locationResults.SelectMany(result => result.RmsG).ToArray(),
                    locationResults
                        .Select(result => new ImuDisplayValueSegment(result.Times, result.RmsG))
                        .ToArray()));
            }

            if (entry.Key == 0)
            {
                var frameResults = locationResults
                    .Select(result => result.FramePitchRoll)
                    .Where(result => result is not null)
                    .Cast<FramePitchRollSeries>()
                    .ToArray();
                if (frameResults.Length > 0)
                {
                    framePitchRoll = new FramePitchRollSeries(
                        frameResults.SelectMany(result => result.Times).ToArray(),
                        frameResults.SelectMany(result => result.PitchDegrees).ToArray(),
                        frameResults.SelectMany(result => result.RollDegrees).ToArray(),
                        frameResults
                            .Select(result => new FramePitchRollSegment(result.Times, result.PitchDegrees, result.RollDegrees))
                            .ToArray());
                }
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

    private static LocationProcessResult ProcessLocation(ImuSampleSegment samples, double sampleRateHz,
        ImuMetaEntry meta, bool includePitchRoll, TrackMotion? track)
    {
        var attitudes = includePitchRoll && meta.GyroLsbPerDps > 0
            ? FrameAttitudeEstimator.Recorded(samples, sampleRateHz, meta, track) : [];
        var rmsWindow = CalculateRmsWindowSamples(sampleRateHz);
        var rmsState = new RollingRmsState(rmsWindow);
        var times = new double[samples.Count];
        var rms = new double[samples.Count];
        var gravity = AccelVector.BikeLevelGravity;
        var hasGravity = false;
        var start = samples.FirstMonotonicDeltaUs / 1e6;
        for (var i = 0; i < samples.Count; i++)
        {
            var dtSeconds = i == 0 ? 0 : 1 / sampleRateHz;
            var accel = ConvertAccel(samples[i], meta.AccelLsbPerG);
            gravity = hasGravity
                ? UpdateGravity(gravity, accel, dtSeconds)
                : EstimateInitialGravity(accel);
            hasGravity = true;
            var dynamic = accel - gravity;
            var dynamicMagnitude = dynamic.Magnitude;

            times[i] = start + i / sampleRateHz;
            rms[i] = rmsState.Add(dynamicMagnitude);
        }
        var pitchRoll = attitudes.Length > 0
            ? new FramePitchRollSeries(times, attitudes.Select(a => a.PitchDegrees).ToArray(), attitudes.Select(a => a.RollDegrees).ToArray())
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

    private static Dictionary<byte, List<ImuSampleSegment>> BuildSampleSegmentsByLocation(RawImuData imuData)
    {
        var samplesByLocation = new Dictionary<byte, List<ImuSampleSegment>>();
        foreach (var location in imuData.ActiveLocations)
        {
            samplesByLocation.TryAdd(location, []);
        }

        var sampleSegments = imuData.SampleSegments;
        var orderedSegments = new List<ImuSampleSegment>(sampleSegments.Count);
        for (var index = 0; index < sampleSegments.Count; index++)
        {
            orderedSegments.Add(sampleSegments[index]);
        }

        foreach (var segment in orderedSegments.OrderBy(segment => segment.FirstMonotonicDeltaUs))
        {
            if (!samplesByLocation.TryGetValue(segment.LocationId, out var segments))
            {
                segments = [];
                samplesByLocation[segment.LocationId] = segments;
            }

            segments.Add(segment);
        }

        return samplesByLocation;
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

internal readonly record struct AccelVector(double X, double Y, double Z)
{
    public static AccelVector BikeLevelGravity { get; } = new(0, 0, 1);

    public double Magnitude => Math.Sqrt(X * X + Y * Y + Z * Z);

    public static AccelVector operator -(AccelVector left, AccelVector right)
    {
        return new AccelVector(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }
}

internal sealed record LocationProcessResult(double[] Times, double[] RmsG, FramePitchRollSeries? FramePitchRoll)
{
    public static readonly LocationProcessResult Empty = new([], [], null);
}
