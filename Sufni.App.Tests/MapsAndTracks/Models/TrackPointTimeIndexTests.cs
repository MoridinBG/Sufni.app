using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.MapsAndTracks.Models;

namespace Sufni.App.Tests.MapsAndTracks.Models;

public class TrackPointTimeIndexTests
{
    [Fact]
    public void TimelineContext_UsesSortedFiniteTimes()
    {
        var points = new[]
        {
            Point(double.NaN),
            Point(20),
            Point(10),
            Point(double.PositiveInfinity),
        };

        var index = new TrackPointTimeIndex(points);

        Assert.Equal(new TrackTimeRange(10, 10), index.TimelineContext);
    }

    [Fact]
    public void FindClosest_UsesNearestFinitePoint()
    {
        var points = new[]
        {
            Point(0),
            Point(double.NaN),
            Point(2),
            Point(4),
        };

        var index = new TrackPointTimeIndex(points);

        Assert.Same(points[2], index.FindClosest(2.6));
    }

    [Fact]
    public void FindClosest_PrefersEarlierPointOnTie()
    {
        var points = new[]
        {
            Point(0),
            Point(2),
        };

        var index = new TrackPointTimeIndex(points);

        Assert.Same(points[0], index.FindClosest(1));
    }

    [Fact]
    public void GetRangeWithBoundaryNeighbors_IncludesNearestOutsidePoints()
    {
        var points = new[]
        {
            Point(0),
            Point(1),
            Point(double.PositiveInfinity),
            Point(2),
            Point(3),
            Point(4),
        };

        var index = new TrackPointTimeIndex(points);

        var result = index.GetRangeWithBoundaryNeighbors(startSeconds: 1.5, endSeconds: 2.5);

        Assert.Equal([1, 2, 3], result.Select(point => point.Time));
    }

    [Fact]
    public void GetRangeWithBoundaryNeighbors_ReturnsBoundaryPairBetweenSamples()
    {
        var points = new[]
        {
            Point(0),
            Point(10),
        };

        var index = new TrackPointTimeIndex(points);

        var result = index.GetRangeWithBoundaryNeighbors(startSeconds: 2, endSeconds: 8);

        Assert.Equal([0, 10], result.Select(point => point.Time));
    }

    private static TrackPoint Point(double time) => new(time, x: time, y: time, elevation: null);
}
