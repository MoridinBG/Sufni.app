using Mapsui.Projections;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Sessions.Models;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.TestSupport.Fixtures;

public static class SyntheticRideTracks
{
    public static (TrackSnapshot Track, IReadOnlyList<TrackPoint> Points) FullTrack(
        SyntheticRide ride, SessionTrackTimeline timeline, double latitude = 42.6)
    {
        const double earthRadius = 6378137;
        var points = new List<TrackPoint>();
        foreach (var fix in ride.Fixes)
        {
            var lat = latitude + fix.North / earthRadius * 180 / Math.PI;
            var lon = 23 + fix.East / (earthRadius * Math.Cos(latitude * Math.PI / 180)) * 180 / Math.PI;
            var (x, y) = SphericalMercator.FromLonLat(lon, lat);
            points.Add(new TrackPoint(timeline.ToTrackSeconds(fix.Seconds), x, y, fix.Elevation));
        }
        return (new TrackSnapshot(Guid.NewGuid(), points[0].Time, points[^1].Time, 1), points);
    }
}
