using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Mapsui.Projections;
using MathNet.Numerics;
using MathNet.Numerics.Interpolation;
using SQLite;

namespace Sufni.App.Models;

public class TrackPoint(long time, double x, double y, double? elevation)
{
    [JsonPropertyName("time")] public long Time { get; set; } = time;
    [JsonPropertyName("x")] public double X { get; set; } = x;
    [JsonPropertyName("y")] public double Y { get; set; } = y;
    [JsonPropertyName("ele")] public double? Elevation { get; set; } = elevation;
}

[Table("track")]
public class Track : Synchronizable
{
    [JsonPropertyName("points")]
    [Ignore]
    public List<TrackPoint> Points { get; set; } = [];

    [JsonIgnore]
    [Column("points")]
    public string PointsJson
    {
        get => AppJson.Serialize(Points);
        set => Points = AppJson.Deserialize<List<TrackPoint>>(value) ?? [];
    }

    [JsonIgnore]
    [Column("start_time")]
    public long StartTime
    {
        get => Points[0].Time;
        set => _ = value;
    }

    [JsonIgnore]
    [Column("end_time")]
    public long EndTime
    {
        get => Points[^1].Time;
        set => _ = value;
    }

    public static Track FromGpx(string gpx)
    {
        return new Track
        {
            Points = ParseGpx(gpx)
        };
    }

    private static List<TrackPoint> ParseGpx(string gpxData)
    {
        var points = new List<TrackPoint>();

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
            if (!string.IsNullOrEmpty(elevationString))
            {
                double.TryParse(elevationString, CultureInfo.InvariantCulture, out var ele);
                elevation = ele;
            }

            var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
            points.Add(new TrackPoint(new DateTimeOffset(time).ToUnixTimeSeconds(), mx, my, elevation));
        }

        return points;
    }

    public List<TrackPoint> GenerateSessionTrack(long start, long end)
    {
        var indices = Points
            .Select((tp, idx) => (tp.Time, idx))
            .Where(x => x.Time >= start && x.Time <= end)
            .Select(x => x.idx)
            .ToArray();

        if (indices.Length == 0) return [];

        var startIdx = indices.First();
        var endIdx = indices.Last() + 1;

        var session = Points.Skip(startIdx).Take(endIdx - startIdx).ToList();
        var sessionTimes = session.Select(tp => (double)(tp.Time - start)).ToArray();
        sessionTimes[0] = 0;

        var x = Generate.LinearSpaced(
            Convert.ToInt32(sessionTimes.Last() / 0.1),
            0,
            sessionTimes.Last());

        var xInterpolate = CubicSpline.InterpolatePchip(sessionTimes, session.Select(tp => tp.X).ToArray());
        var yInterpolate = CubicSpline.InterpolatePchip(sessionTimes, session.Select(tp => tp.Y).ToArray());

        var interpolated = x.Select(t =>
            new TrackPoint(
                start + (long)Math.Round(t),
                xInterpolate.Interpolate(t),
                yInterpolate.Interpolate(t),
                0
            )).ToList();

        return interpolated;
    }
}
