using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Segments.Models;
using Sufni.Telemetry;

namespace Sufni.App.Segments.Services;

/// <summary>
/// Builds a <see cref="SegmentComparison"/> from a match and the matched session's
/// telemetry and track. Signals are resampled at 100 Hz of source time.
/// </summary>
internal static class SegmentComparisonBuilder
{
    private const int ComparisonSampleHz = 100;

    public static SegmentComparison? Build(
        SegmentMatch match,
        TelemetryData matchedTelemetry,
        double sourceDurationSeconds,
        IReadOnlyList<TrackPoint>? matchedTrackPoints = null,
        TrackTimeRange? matchedTrackTimelineContext = null)
    {
        if (match.TimeMap.Count < 2 ||
            matchedTelemetry.Metadata.SampleRate <= 0 ||
            sourceDurationSeconds <= 0 ||
            !double.IsFinite(sourceDurationSeconds))
        {
            return null;
        }

        var sampledMap = ResampleTimeMap(match.TimeMap);
        if (sampledMap.Count < 2)
        {
            return null;
        }

        var sourceSeconds = sampledMap
            .Select(point => point.SourceSeconds)
            .ToArray();

        var travelSeries = new List<SegmentComparisonSeries>();
        AddSuspensionSeries(
            travelSeries,
            matchedTelemetry.Front,
            suspension => suspension.Travel,
            "Front",
            "mm",
            SegmentComparisonSeriesRole.FrontTravel,
            "0.#",
            sourceSeconds,
            sampledMap,
            matchedTelemetry.Metadata.SampleRate,
            valueScale: 1);
        AddSuspensionSeries(
            travelSeries,
            matchedTelemetry.Rear,
            suspension => suspension.Travel,
            "Rear",
            "mm",
            SegmentComparisonSeriesRole.RearTravel,
            "0.#",
            sourceSeconds,
            sampledMap,
            matchedTelemetry.Metadata.SampleRate,
            valueScale: 1);

        var velocitySeries = new List<SegmentComparisonSeries>();
        AddSuspensionSeries(
            velocitySeries,
            matchedTelemetry.Front,
            suspension => suspension.Velocity,
            "Front",
            "m/s",
            SegmentComparisonSeriesRole.FrontVelocity,
            "0.###",
            sourceSeconds,
            sampledMap,
            matchedTelemetry.Metadata.SampleRate,
            valueScale: 1.0 / 1000);
        AddSuspensionSeries(
            velocitySeries,
            matchedTelemetry.Rear,
            suspension => suspension.Velocity,
            "Rear",
            "m/s",
            SegmentComparisonSeriesRole.RearVelocity,
            "0.###",
            sourceSeconds,
            sampledMap,
            matchedTelemetry.Metadata.SampleRate,
            valueScale: 1.0 / 1000);

        var imuSeries = BuildImuSeries(matchedTelemetry.ImuData, sourceSeconds, sampledMap);
        var speedSeries = BuildSpeedSeries(matchedTrackPoints, matchedTrackTimelineContext, sourceSeconds, sampledMap);
        if (travelSeries.Count == 0 &&
            velocitySeries.Count == 0 &&
            imuSeries.Count == 0 &&
            speedSeries.Count == 0)
        {
            return null;
        }

        TelemetryTimeRange? analysisComparisonRange = null;
        if (TelemetryTimeRange.TryCreateClamped(
                match.StartSeconds,
                match.EndSeconds,
                matchedTelemetry.Metadata.Duration,
                out var clampedRange))
        {
            analysisComparisonRange = clampedRange;
        }

        return new SegmentComparison(
            match,
            sourceDurationSeconds,
            travelSeries,
            velocitySeries,
            imuSeries,
            speedSeries,
            BuildAirtimes(match.TimeMap, matchedTelemetry.Airtimes),
            BuildMatchedSegmentTrackPoints(
                matchedTrackPoints,
                matchedTrackTimelineContext,
                match.StartSeconds,
                match.EndSeconds),
            analysisComparisonRange);
    }

    public static SegmentComparisonDetail BuildDetail(
        TelemetryData matchedTelemetry,
        IReadOnlyList<TrackPoint>? matchedTrackPoints) =>
        new(matchedTelemetry, BuildMatchedSessionTrackPoints(matchedTrackPoints));

    private static void AddSuspensionSeries(
        ICollection<SegmentComparisonSeries> series,
        Suspension suspension,
        Func<Suspension, ImmutableArray<double>> valuesSelector,
        string label,
        string unit,
        SegmentComparisonSeriesRole role,
        string format,
        double[] sourceSeconds,
        IReadOnlyList<SegmentTimeMapPoint> timeMap,
        int sampleRate,
        double valueScale)
    {
        var sourceValues = valuesSelector(suspension);
        if (!suspension.Present || sourceValues.IsDefaultOrEmpty)
        {
            return;
        }

        var values = new double[timeMap.Count];
        for (var i = 0; i < timeMap.Count; i++)
        {
            values[i] = SampleRegularValues(sourceValues, timeMap[i].MatchSeconds, sampleRate) * valueScale;
        }

        series.Add(new SegmentComparisonSeries(
            label,
            unit,
            role,
            (double[])sourceSeconds.Clone(),
            values,
            format));
    }

    private static IReadOnlyList<SegmentComparisonSeries> BuildImuSeries(
        RawImuData? imuData,
        double[] sourceSeconds,
        IReadOnlyList<SegmentTimeMapPoint> timeMap)
    {
        if (imuData is null ||
            imuData.SampleRate <= 0 ||
            !imuData.HasSamples ||
            imuData.ActiveLocations.Count == 0)
        {
            return [];
        }

        var signals = new Dictionary<byte, List<ImuSignalSegment>>();
        foreach (var location in imuData.ActiveLocations)
        {
            signals[location] = [];
        }

        var metaByLocation = new Dictionary<byte, ImuMetaEntry>();
        foreach (var meta in imuData.Meta)
        {
            metaByLocation.TryAdd(meta.LocationId, meta);
        }

        var sampleSegments = imuData.SampleSegments;
        var orderedSegments = new List<ImuSampleSegment>(sampleSegments.Count);
        for (var index = 0; index < sampleSegments.Count; index++)
        {
            orderedSegments.Add(sampleSegments[index]);
        }

        foreach (var segment in orderedSegments.OrderBy(segment => segment.FirstMonotonicDeltaUs))
        {
            if (segment.Count == 0 ||
                !metaByLocation.TryGetValue(segment.LocationId, out var meta))
            {
                continue;
            }

            if (!signals.TryGetValue(segment.LocationId, out var locationSegments))
            {
                locationSegments = [];
                signals[segment.LocationId] = locationSegments;
            }

            var scale = meta.AccelLsbPerG == 0 ? 1.0 : meta.AccelLsbPerG;
            var values = new double[segment.Count];
            for (var index = 0; index < segment.Count; index++)
            {
                var record = segment[index];
                var ax = record.Ax / scale;
                var ay = record.Ay / scale;
                var az = record.Az / scale - 1.0;
                values[index] = Math.Sqrt(ax * ax + ay * ay + az * az);
            }

            locationSegments.Add(new ImuSignalSegment(
                segment.FirstMonotonicDeltaUs / 1_000_000.0,
                values));
        }

        var series = new List<SegmentComparisonSeries>();
        foreach (var (location, segments) in signals.OrderBy(item => item.Key))
        {
            if (segments.Count == 0)
            {
                continue;
            }

            var runs = BuildMappedImuRuns(
                segments,
                sourceSeconds,
                timeMap,
                imuData.SampleRate);
            if (runs.Count == 0)
            {
                continue;
            }

            var (label, role) = location switch
            {
                0 => ("Frame", SegmentComparisonSeriesRole.FrameImu),
                1 => ("Fork", SegmentComparisonSeriesRole.ForkImu),
                2 => ("Shock", SegmentComparisonSeriesRole.ShockImu),
                _ => ($"Location {location}", SegmentComparisonSeriesRole.FrameImu),
            };
            series.Add(new SegmentComparisonSeries(
                label,
                "g",
                role,
                [],
                [],
                "0.###",
                runs));
        }

        return series;
    }

    private static IReadOnlyList<SegmentComparisonSeriesRun> BuildMappedImuRuns(
        IReadOnlyList<ImuSignalSegment> segments,
        IReadOnlyList<double> sourceSeconds,
        IReadOnlyList<SegmentTimeMapPoint> timeMap,
        int sampleRate)
    {
        var runs = new List<SegmentComparisonSeriesRun>();
        var runSeconds = new List<double>();
        var runValues = new List<double>();

        for (var index = 0; index < timeMap.Count; index++)
        {
            if (TrySampleImuSegments(segments, timeMap[index].MatchSeconds, sampleRate, out var value))
            {
                runSeconds.Add(sourceSeconds[index]);
                runValues.Add(value);
                continue;
            }

            AddCompletedRun(runs, runSeconds, runValues);
        }

        AddCompletedRun(runs, runSeconds, runValues);
        return runs;
    }

    private static void AddCompletedRun(
        ICollection<SegmentComparisonSeriesRun> runs,
        List<double> sourceSeconds,
        List<double> values)
    {
        if (sourceSeconds.Count >= 2)
        {
            runs.Add(new SegmentComparisonSeriesRun(sourceSeconds.ToArray(), values.ToArray()));
        }

        sourceSeconds.Clear();
        values.Clear();
    }

    private static bool TrySampleImuSegments(
        IReadOnlyList<ImuSignalSegment> segments,
        double seconds,
        int sampleRate,
        out double value)
    {
        value = default;
        foreach (var segment in segments)
        {
            var endSeconds = segment.StartSeconds + (segment.Values.Length - 1) / (double)sampleRate;
            if (seconds < segment.StartSeconds || seconds > endSeconds)
            {
                continue;
            }

            value = SampleRegularValues(segment.Values, seconds - segment.StartSeconds, sampleRate);
            return true;
        }

        return false;
    }

    private static IReadOnlyList<SegmentComparisonSeries> BuildSpeedSeries(
        IReadOnlyList<TrackPoint>? trackPoints,
        TrackTimeRange? timelineContext,
        double[] sourceSeconds,
        IReadOnlyList<SegmentTimeMapPoint> timeMap)
    {
        if (trackPoints is not { Count: >= 2 } || timelineContext is null)
        {
            return [];
        }

        var sampleRuns = BuildSpeedSampleRuns(trackPoints, timelineContext.Value.OriginSeconds);
        if (sampleRuns.Count == 0)
        {
            return [];
        }

        var runs = BuildMappedSpeedRuns(sampleRuns, sourceSeconds, timeMap);
        return runs.Count == 0
            ? []
            :
            [
                new SegmentComparisonSeries(
                    "Speed",
                    "km/h",
                    SegmentComparisonSeriesRole.Speed,
                    [],
                    [],
                    "0.#",
                    runs)
            ];
    }

    private static IReadOnlyList<IReadOnlyList<(double Seconds, double Value)>> BuildSpeedSampleRuns(
        IReadOnlyList<TrackPoint> trackPoints,
        double originSeconds)
    {
        var runs = new List<IReadOnlyList<(double Seconds, double Value)>>();
        var currentRun = new List<(double Seconds, double Value)>();
        foreach (var point in trackPoints.OrderBy(point => point.Time))
        {
            var seconds = point.Time - originSeconds;
            if (!double.IsFinite(seconds) ||
                point.Speed is not { } speed ||
                !double.IsFinite(speed))
            {
                AddCompletedSpeedSampleRun(runs, currentRun);
                continue;
            }

            currentRun.Add((seconds, speed * 3.6));
        }

        AddCompletedSpeedSampleRun(runs, currentRun);
        return runs;
    }

    private static void AddCompletedSpeedSampleRun(
        ICollection<IReadOnlyList<(double Seconds, double Value)>> runs,
        List<(double Seconds, double Value)> currentRun)
    {
        if (currentRun.Count >= 2)
        {
            runs.Add(currentRun.ToArray());
        }

        currentRun.Clear();
    }

    private static IReadOnlyList<SegmentComparisonSeriesRun> BuildMappedSpeedRuns(
        IReadOnlyList<IReadOnlyList<(double Seconds, double Value)>> sampleRuns,
        IReadOnlyList<double> sourceSeconds,
        IReadOnlyList<SegmentTimeMapPoint> timeMap)
    {
        var runs = new List<SegmentComparisonSeriesRun>();
        var runSeconds = new List<double>();
        var runValues = new List<double>();
        var sampleIndexes = Enumerable.Repeat(1, sampleRuns.Count).ToArray();

        for (var index = 0; index < timeMap.Count; index++)
        {
            if (TrySampleSpeedRuns(
                    sampleRuns,
                    sampleIndexes,
                    timeMap[index].MatchSeconds,
                    out var value))
            {
                runSeconds.Add(sourceSeconds[index]);
                runValues.Add(value);
                continue;
            }

            AddCompletedRun(runs, runSeconds, runValues);
        }

        AddCompletedRun(runs, runSeconds, runValues);
        return runs;
    }

    private static bool TrySampleSpeedRuns(
        IReadOnlyList<IReadOnlyList<(double Seconds, double Value)>> sampleRuns,
        int[] sampleIndexes,
        double seconds,
        out double value)
    {
        value = default;
        for (var runIndex = 0; runIndex < sampleRuns.Count; runIndex++)
        {
            var samples = sampleRuns[runIndex];
            if (seconds < samples[0].Seconds || seconds > samples[^1].Seconds)
            {
                continue;
            }

            return TrySampleExplicitValues(
                samples,
                seconds,
                ref sampleIndexes[runIndex],
                out value);
        }

        return false;
    }

    private static IReadOnlyList<SegmentMatchedAirtime> BuildAirtimes(
        IReadOnlyList<SegmentTimeMapPoint> timeMap,
        IReadOnlyList<Airtime> airtimes)
    {
        if (airtimes.Count == 0)
        {
            return [];
        }

        var matchStart = timeMap[0].MatchSeconds;
        var matchEnd = timeMap[^1].MatchSeconds;
        var mapped = new List<SegmentMatchedAirtime>();
        foreach (var airtime in airtimes)
        {
            if (!double.IsFinite(airtime.Start) ||
                !double.IsFinite(airtime.End))
            {
                continue;
            }

            var start = Math.Max(Math.Min(airtime.Start, airtime.End), matchStart);
            var end = Math.Min(Math.Max(airtime.Start, airtime.End), matchEnd);
            if (end <= start)
            {
                continue;
            }

            mapped.Add(new SegmentMatchedAirtime(
                MapMatchSecondsToSourceSeconds(timeMap, start),
                MapMatchSecondsToSourceSeconds(timeMap, end)));
        }

        return mapped;
    }

    private static IReadOnlyList<TrackPoint> BuildMatchedSegmentTrackPoints(
        IReadOnlyList<TrackPoint>? trackPoints,
        TrackTimeRange? timelineContext,
        double matchStartSeconds,
        double matchEndSeconds)
    {
        if (trackPoints is not { Count: >= 2 } || timelineContext is null)
        {
            return [];
        }

        var startSeconds = timelineContext.Value.OriginSeconds + Math.Min(matchStartSeconds, matchEndSeconds);
        var endSeconds = timelineContext.Value.OriginSeconds + Math.Max(matchStartSeconds, matchEndSeconds);
        if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds) || startSeconds >= endSeconds)
        {
            return [];
        }

        var points = trackPoints
            .Where(point => double.IsFinite(point.Time) &&
                            double.IsFinite(point.X) &&
                            double.IsFinite(point.Y) &&
                            point.Time >= startSeconds &&
                            point.Time <= endSeconds)
            .OrderBy(point => point.Time)
            .ToArray();

        return points.Length >= 2 ? points : [];
    }

    private static IReadOnlyList<TrackPoint> BuildMatchedSessionTrackPoints(IReadOnlyList<TrackPoint>? trackPoints)
    {
        if (trackPoints is not { Count: >= 2 })
        {
            return [];
        }

        var points = trackPoints
            .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y))
            .OrderBy(point => point.Time)
            .ToArray();

        return points.Length >= 2 ? points : [];
    }

    private static double MapMatchSecondsToSourceSeconds(
        IReadOnlyList<SegmentTimeMapPoint> timeMap,
        double matchSeconds)
    {
        if (matchSeconds <= timeMap[0].MatchSeconds)
        {
            return timeMap[0].SourceSeconds;
        }

        for (var index = 1; index < timeMap.Count; index++)
        {
            var previous = timeMap[index - 1];
            var next = timeMap[index];
            if (matchSeconds > next.MatchSeconds)
            {
                continue;
            }

            var matchSpan = next.MatchSeconds - previous.MatchSeconds;
            var ratio = matchSpan > 0
                ? (matchSeconds - previous.MatchSeconds) / matchSpan
                : 0;
            return previous.SourceSeconds + (next.SourceSeconds - previous.SourceSeconds) * ratio;
        }

        return timeMap[^1].SourceSeconds;
    }

    private static IReadOnlyList<SegmentTimeMapPoint> ResampleTimeMap(
        IReadOnlyList<SegmentTimeMapPoint> timeMap)
    {
        var sourceStart = timeMap[0].SourceSeconds;
        var sourceEnd = timeMap[^1].SourceSeconds;
        if (!double.IsFinite(sourceStart) ||
            !double.IsFinite(sourceEnd) ||
            sourceEnd <= sourceStart)
        {
            return [];
        }

        var stepSeconds = 1.0 / ComparisonSampleHz;
        var sampleCount = Math.Max(2, (int)Math.Ceiling((sourceEnd - sourceStart) / stepSeconds) + 1);
        var samples = new List<SegmentTimeMapPoint>(sampleCount);
        var mapIndex = 1;

        for (var index = 0; index < sampleCount; index++)
        {
            var sourceSeconds = index == sampleCount - 1
                ? sourceEnd
                : Math.Min(sourceStart + index * stepSeconds, sourceEnd);

            while (mapIndex < timeMap.Count - 1 && timeMap[mapIndex].SourceSeconds < sourceSeconds)
            {
                mapIndex++;
            }

            var previous = timeMap[mapIndex - 1];
            var next = timeMap[mapIndex];
            var sourceSpan = next.SourceSeconds - previous.SourceSeconds;
            var ratio = sourceSpan > 0
                ? (sourceSeconds - previous.SourceSeconds) / sourceSpan
                : 0;
            samples.Add(new SegmentTimeMapPoint(
                sourceSeconds,
                previous.MatchSeconds + (next.MatchSeconds - previous.MatchSeconds) * ratio));
        }

        return samples;
    }

    private static double SampleRegularValues(IReadOnlyList<double> values, double seconds, int sampleRate)
    {
        var samplePosition = Math.Clamp(seconds * sampleRate, 0, values.Count - 1);
        var lowerIndex = (int)Math.Floor(samplePosition);
        var upperIndex = (int)Math.Ceiling(samplePosition);
        if (lowerIndex == upperIndex)
        {
            return values[lowerIndex];
        }

        var ratio = samplePosition - lowerIndex;
        return values[lowerIndex] + (values[upperIndex] - values[lowerIndex]) * ratio;
    }

    private readonly record struct ImuSignalSegment(double StartSeconds, double[] Values);

    private static bool TrySampleExplicitValues(
        IReadOnlyList<(double Seconds, double Value)> samples,
        double seconds,
        ref int sampleIndex,
        out double value)
    {
        value = default;
        if (seconds < samples[0].Seconds || seconds > samples[^1].Seconds)
        {
            return false;
        }

        while (sampleIndex < samples.Count - 1 && samples[sampleIndex].Seconds < seconds)
        {
            sampleIndex++;
        }

        var previous = samples[Math.Max(0, sampleIndex - 1)];
        var next = samples[sampleIndex];
        var span = next.Seconds - previous.Seconds;
        var ratio = span > 0
            ? (seconds - previous.Seconds) / span
            : 0;
        value = previous.Value + (next.Value - previous.Value) * ratio;
        return true;
    }
}
