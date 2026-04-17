using System;
using System.Collections.Generic;
using System.Linq;
using Mapsui.Projections;
using Sufni.Telemetry;

namespace Sufni.App.Models;

internal static class GpsTrackPointProjection
{
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
            record.Altitude);
    }

    public static List<TrackPoint> ProjectAll(IEnumerable<GpsRecord> records)
    {
        return records
            .OrderBy(record => record.Timestamp)
            .Select(TryProject)
            .OfType<TrackPoint>()
            .ToList();
    }
}