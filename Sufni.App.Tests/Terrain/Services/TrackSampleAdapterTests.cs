using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Terrain.Services;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Terrain.Services;

public class TrackSampleAdapterTests
{
    [Fact]
    public void EastDisplacementUsesGroundMetresAtTheRideLatitude()
    {
        var timeline = new SessionTrackTimeline(1000, 1.5);
        var ride = SyntheticRide.Build([new RideSection(100, 10)]);
        var (_, fixes) = SyntheticRideTracks.FullTrack(ride, timeline, latitude: 42.6);
        var track = TrackSampleAdapter.FromOriginalFixes(fixes, timeline, 10, null);
        Assert.InRange(track[^1].East, 99.99, 100.01);
        Assert.Equal(10, track[^1].Seconds);
    }

    [Fact]
    public void OnlyOriginalFixesInsideTheAlignedRecordingWindowAreRetained()
    {
        var timeline = new SessionTrackTimeline(100, 2);
        TrackPoint[] points = [new(101, 0, 0, null), new(104.25, 12, 0, 2),
            new(105, double.NaN, 0, 4), new(111.5, 15, 0, null), new(113, 20, 0, null)];
        var track = TrackSampleAdapter.FromOriginalFixes(points, timeline, 10, null);
        Assert.Equal([2.25, 9.5], track.Select(p => p.Seconds));
        Assert.Equal(2, track[0].Elevation);
        Assert.Null(track[1].Elevation);
    }

    [Fact]
    public void OnlyAFixWithAReceiverRecordAtItsMillisecondCarriesTheReceiverSpeed()
    {
        var timeline = new SessionTrackTimeline(1_700_000_000);
        TrackPoint[] points = [new(1_700_000_001.2, 0, 0, null), new(1_700_000_001.4, 2, 0, null)];
        var start = DateTime.UnixEpoch.AddSeconds(1_700_000_001);
        GpsRecord Record(int milliseconds) => new(start.AddMilliseconds(milliseconds), 42.6, 23, 500, 8.5f, 90, 3, 9, 1, 2);
        var track = TrackSampleAdapter.FromOriginalFixes(points, timeline, 10, [Record(200), Record(401)]);
        Assert.Equal([8.5, null], track.Select(p => p.ReceiverSpeed));
    }
}
