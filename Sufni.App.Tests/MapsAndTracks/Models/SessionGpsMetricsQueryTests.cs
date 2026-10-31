using Sufni.App.MapsAndTracks.Queries;
using Sufni.App.MapsAndTracks.Stores;

namespace Sufni.App.Tests.MapsAndTracks.Models;

public class SessionGpsMetricsQueryTests
{
    [Fact]
    public void Evaluate_UsesContentIdentityForMetrics_AndLocalRevisionOnlyForPointReads()
    {
        var track = new TrackSnapshot(Guid.NewGuid(), 99, 110, 1);
        var first = SessionGpsMetricsQuery.Evaluate(100, 2, 0.5, 1.25, [track]);
        var otherDevice = SessionGpsMetricsQuery.Evaluate(100, 2, 0.5, 1.25, [track with { PointsRevision = 9 }]);
        var aligned = SessionGpsMetricsQuery.Evaluate(100, 2, 1, 1.25, [track]);

        Assert.Equal(first.Key, otherDevice.Key);
        Assert.NotEqual(first.Track, otherDevice.Track);
        Assert.Equal(0.25, first.Key.WindowOriginFraction);
        Assert.Equal(0.25, first.Track!.Value.WindowOriginFraction);
        Assert.NotEqual(first.Key, aligned.Key);
    }
}
