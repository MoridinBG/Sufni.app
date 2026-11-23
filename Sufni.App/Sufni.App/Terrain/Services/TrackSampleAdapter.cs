using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Mapsui.Projections;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Services;

public static class TrackSampleAdapter
{
    public static ImmutableArray<TrackSample> FromOriginalFixes(IReadOnlyList<TrackPoint> points,
        SessionTrackTimeline timeline, double durationSeconds)
    {
        var result = ImmutableArray.CreateBuilder<TrackSample>();
        TrackPoint? origin = null;
        var scale = 0d;
        foreach (var point in points)
        {
            var seconds = timeline.ToTelemetrySeconds(point.Time);
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                !double.IsFinite(seconds) || !(seconds >= 0 && seconds <= durationSeconds)) continue;
            if (origin is null)
            {
                origin = point;
                var (_, latitude) = SphericalMercator.ToLonLat(point.X, point.Y);
                scale = Math.Cos(latitude * Math.PI / 180);
            }
            result.Add(new(seconds, (point.X - origin.X) * scale, (point.Y - origin.Y) * scale,
                point.Elevation is { } height && double.IsFinite(height) ? height : null));
        }
        return result.ToImmutable();
    }
}
