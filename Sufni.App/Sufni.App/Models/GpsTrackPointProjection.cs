using System;
using System.Collections.Generic;
using System.Linq;
using Mapsui.Projections;
using Sufni.Telemetry;

namespace Sufni.App.Models;

internal static class GpsTrackPointProjection
{
    public const int ProjectionVersion = 1;

    public static TrackPoint? TryProject(GpsRecord record)
    {
        if (record.FixMode <= 0
            || !double.IsFinite(record.Latitude)
            || !double.IsFinite(record.Longitude)
            || !float.IsFinite(record.Altitude))
        {
            return null;
        }

        var (mx, my) = SphericalMercator.FromLonLat(record.Longitude, record.Latitude);
        return new TrackPoint(
            new DateTimeOffset(record.Timestamp).ToUnixTimeMilliseconds() / 1000.0,
            mx,
            my,
            record.Altitude,
            fixMode: record.FixMode,
            satellites: record.Satellites,
            epe2d: record.Epe2d,
            epe3d: record.Epe3d);
    }

    public static List<TrackPoint> ProjectAll(IEnumerable<GpsRecord> records)
    {
        var points = new List<TrackPoint>();
        var coordinates = new List<TrackPointGeoCoordinate>();

        foreach (var record in records.OrderBy(record => record.Timestamp))
        {
            var point = TryProject(record);
            if (point is null)
            {
                continue;
            }

            points.Add(point);
            coordinates.Add(new TrackPointGeoCoordinate(record.Latitude, record.Longitude));
        }

        TrackPointSeries.CalculateSpeed(points, coordinates);
        return points;
    }
}
