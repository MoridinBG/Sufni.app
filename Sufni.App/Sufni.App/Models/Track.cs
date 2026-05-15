using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Mapsui.Projections;
using MathNet.Numerics.Interpolation;
using SQLite;
using Sufni.Telemetry;

namespace Sufni.App.Models;

public class TrackPoint(
    double time,
    double x,
    double y,
    double? elevation,
    double? speed = null,
    byte? fixMode = null,
    byte? satellites = null,
    float? epe2d = null,
    float? epe3d = null)
{
    [JsonPropertyName("time")] public double Time { get; set; } = time;
    [JsonPropertyName("x")] public double X { get; set; } = x;
    [JsonPropertyName("y")] public double Y { get; set; } = y;
    [JsonPropertyName("ele")] public double? Elevation { get; set; } = elevation;
    [JsonPropertyName("spd")] public double? Speed { get; set; } = speed;
    [JsonPropertyName("fix")] public byte? FixMode { get; set; } = fixMode;
    [JsonPropertyName("sats")] public byte? Satellites { get; set; } = satellites;
    [JsonPropertyName("epe2d")] public float? Epe2d { get; set; } = epe2d;
    [JsonPropertyName("epe3d")] public float? Epe3d { get; set; } = epe3d;
}

[Table("track")]
public class Track : Synchronizable
{
    private const double SessionTrackSampleStepSeconds = 0.1;
    private const double SessionTrackTimeMergeToleranceSeconds = 1e-9;
    private List<TrackPoint> points = [];

    [JsonIgnore]
    public bool HasPoints => points.Count > 0;

    [JsonPropertyName("points")]
    [Ignore]
    public List<TrackPoint> Points
    {
        get => points;
        set => points = value ?? [];
    }

    [JsonIgnore]
    [Column("points")]
    public string PointsJson
    {
        get => AppJson.Serialize(Points);
        set => points = AppJson.Deserialize<List<TrackPoint>>(value) ?? [];
    }

    [JsonIgnore]
    [Column("start_time")]
    public long StartTime
    {
        get => HasPoints ? (long)Points[0].Time : 0;
        set => _ = value;
    }

    [JsonIgnore]
    [Column("end_time")]
    public long EndTime
    {
        get => HasPoints ? (long)Points[^1].Time : 0;
        set => _ = value;
    }

    public static Track? FromGpx(string gpx)
    {
        var points = ParseGpx(gpx, out var coordinates);
        TrackPointSeries.CalculateSpeed(points, coordinates);
        return points.Count == 0 ? null : new Track
        {
            Points = points
        };
    }

    public static Track? FromGpsRecords(GpsRecord[] records)
    {
        var points = GpsTrackPointProjection.ProjectAll(records);
        return points.Count == 0 ? null : new Track
        {
            Points = points
        };
    }

    private static List<TrackPoint> ParseGpx(string gpxData, out List<TrackPointGeoCoordinate> coordinates)
    {
        var points = new List<TrackPoint>();
        coordinates = [];

        XNamespace ns = "http://www.topografix.com/GPX/1/1";
        var xdoc = XDocument.Parse(gpxData);
        var trkpts = xdoc.Descendants(ns + "trkpt");

        foreach (var pt in trkpts)
        {
            var timeString = pt.Element(ns + "time")?.Value;
            if (string.IsNullOrEmpty(timeString) ||
                !DateTime.TryParse(timeString, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var time))
            {
                continue;
            }

            if (!double.TryParse(pt.Attribute("lat")?.Value, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(pt.Attribute("lon")?.Value, CultureInfo.InvariantCulture, out var lon))
            {
                continue;
            }

            double? elevation = null;
            var elevationString = pt.Element(ns + "ele")?.Value;
            if (!string.IsNullOrEmpty(elevationString) &&
                double.TryParse(elevationString, CultureInfo.InvariantCulture, out var ele))
            {
                elevation = ele;
            }

            var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
            points.Add(new TrackPoint(new DateTimeOffset(time).ToUnixTimeMilliseconds() / 1000.0, mx, my, elevation));
            coordinates.Add(new TrackPointGeoCoordinate(lat, lon));
        }

        return points;
    }

    public List<TrackPoint> GenerateSessionTrack(long start, long end)
    {
        var session = Points
            .Where(tp => tp.Time >= start && tp.Time <= end)
            .Where(IsFiniteInterpolationPoint)
            .OrderBy(tp => tp.Time)
            .ToList();

        session = RemoveDuplicateTimes(session);
        if (session.Count < 2) return [];

        var actualSessionTimes = session.Select(tp => tp.Time - start).ToArray();
        var sessionTimes = actualSessionTimes.ToArray();
        sessionTimes[0] = 0;

        var xInterpolate = CreateCoordinateInterpolator(sessionTimes, session.Select(tp => tp.X).ToArray());
        var yInterpolate = CreateCoordinateInterpolator(sessionTimes, session.Select(tp => tp.Y).ToArray());
        var elevationInterpolate = CreateNullableLinearInterpolator(session, sessionTimes, tp => tp.Elevation);
        var speedInterpolate = CreateNullableLinearInterpolator(session, sessionTimes, tp => tp.Speed);
        var sourcePointsByTime = session
            .Select((point, index) => (
                Time: sessionTimes[index],
                Point: point,
                IsPinnedToSessionStart: index == 0 && actualSessionTimes[index] != sessionTimes[index]))
            .Where(item => !item.IsPinnedToSessionStart)
            .ToDictionary(item => item.Time, item => item.Point);
        var x = CreateSessionTrackTimes(sessionTimes);

        var interpolated = x.Select(t =>
        {
            sourcePointsByTime.TryGetValue(t, out var sourcePoint);
            return new TrackPoint(
                start + t,
                xInterpolate(t),
                yInterpolate(t),
                elevationInterpolate(t),
                speedInterpolate(t),
                sourcePoint?.FixMode,
                sourcePoint?.Satellites,
                sourcePoint?.Epe2d,
                sourcePoint?.Epe3d);
        }).ToList();

        return interpolated;
    }

    private static double[] CreateSessionTrackTimes(IReadOnlyList<double> sourceTimes)
    {
        var duration = sourceTimes[^1];
        var candidates = new List<SessionTrackTimeCandidate>();
        var regularSampleCount = (int)Math.Floor(duration / SessionTrackSampleStepSeconds);
        for (var index = 0; index <= regularSampleCount; index++)
        {
            candidates.Add(new SessionTrackTimeCandidate(index * SessionTrackSampleStepSeconds, IsSourceTime: false));
        }

        candidates.Add(new SessionTrackTimeCandidate(duration, IsSourceTime: false));
        candidates.AddRange(sourceTimes
            .Where(time => double.IsFinite(time) && time >= 0 && time <= duration)
            .Select(time => new SessionTrackTimeCandidate(time, IsSourceTime: true)));

        candidates.Sort(static (left, right) =>
        {
            var comparison = left.Time.CompareTo(right.Time);
            if (comparison != 0) return comparison;
            return right.IsSourceTime.CompareTo(left.IsSourceTime);
        });

        var times = new List<double>(candidates.Count);
        var lastWasSourceTime = false;
        foreach (var candidate in candidates)
        {
            if (times.Count > 0 &&
                Math.Abs(candidate.Time - times[^1]) <= SessionTrackTimeMergeToleranceSeconds)
            {
                if (candidate.IsSourceTime && !lastWasSourceTime)
                {
                    times[^1] = candidate.Time;
                    lastWasSourceTime = true;
                }

                continue;
            }

            times.Add(candidate.Time);
            lastWasSourceTime = candidate.IsSourceTime;
        }

        return times.ToArray();
    }

    private static bool IsFiniteInterpolationPoint(TrackPoint point)
    {
        return double.IsFinite(point.Time)
               && double.IsFinite(point.X)
               && double.IsFinite(point.Y);
    }

    private static List<TrackPoint> RemoveDuplicateTimes(IReadOnlyList<TrackPoint> points)
    {
        var unique = new List<TrackPoint>(points.Count);
        foreach (var point in points)
        {
            if (unique.Count > 0 && point.Time <= unique[^1].Time)
            {
                continue;
            }

            unique.Add(point);
        }

        return unique;
    }

    private static Func<double, double> CreateCoordinateInterpolator(double[] times, double[] values)
    {
        if (times.Length >= 3)
        {
            var spline = CubicSpline.InterpolatePchip(times, values);
            return spline.Interpolate;
        }

        return time => InterpolateLinearValue(times, values, time);
    }

    private static double InterpolateLinearValue(IReadOnlyList<double> times, IReadOnlyList<double> values, double time)
    {
        return InterpolateLinear(times, values, time) ?? values[0];
    }

    private static Func<double, double?> CreateNullableLinearInterpolator(
        IReadOnlyList<TrackPoint> points,
        IReadOnlyList<double> times,
        Func<TrackPoint, double?> valueSelector)
    {
        var samples = points
            .Select((point, index) => (Time: times[index], Value: valueSelector(point)))
            .Where(sample => double.IsFinite(sample.Time)
                             && sample.Value is { } value
                             && double.IsFinite(value))
            .Select(sample => (sample.Time, Value: sample.Value!.Value))
            .ToArray();

        if (samples.Length < 2)
        {
            return _ => null;
        }

        var sampleTimes = samples.Select(sample => sample.Time).ToArray();
        var sampleValues = samples.Select(sample => sample.Value).ToArray();

        return time => InterpolateLinear(sampleTimes, sampleValues, time);
    }

    private static double? InterpolateLinear(IReadOnlyList<double> times, IReadOnlyList<double> values, double time)
    {
        if (time <= times[0])
        {
            return values[0];
        }

        if (time >= times[^1])
        {
            return values[^1];
        }

        for (var i = 1; i < times.Count; i++)
        {
            if (time > times[i])
            {
                continue;
            }

            var lowerTime = times[i - 1];
            var upperTime = times[i];
            var span = upperTime - lowerTime;
            if (!double.IsFinite(span) || span <= 0)
            {
                return null;
            }

            var ratio = (time - lowerTime) / span;
            return values[i - 1] + (values[i] - values[i - 1]) * ratio;
        }

        return values[^1];
    }

    private readonly record struct SessionTrackTimeCandidate(double Time, bool IsSourceTime);
}
