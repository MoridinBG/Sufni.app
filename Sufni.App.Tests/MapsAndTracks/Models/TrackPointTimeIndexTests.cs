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
    public void FindClosest_ClampsToNearestEdgePoint()
    {
        var points = new[]
        {
            Point(10),
            Point(20),
        };

        var index = new TrackPointTimeIndex(points);

        Assert.Same(points[0], index.FindClosest(5));
        Assert.Same(points[1], index.FindClosest(30));
    }

    [Fact]
    public void FindClosest_ReturnsNull_ForEmptyIndexOrNonFiniteTarget()
    {
        var index = new TrackPointTimeIndex([]);

        Assert.Null(index.FindClosest(1));

        index = new TrackPointTimeIndex([Point(1)]);
        Assert.Null(index.FindClosest(double.NaN));
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

    [Fact]
    public void GetRangeWithBoundaryNeighbors_ClampsBoundaryNeighborsToTrackEdges()
    {
        var points = new[]
        {
            Point(10),
            Point(20),
            Point(30),
        };

        var index = new TrackPointTimeIndex(points);

        var beforeStart = index.GetRangeWithBoundaryNeighbors(startSeconds: 0, endSeconds: 15);
        var afterEnd = index.GetRangeWithBoundaryNeighbors(startSeconds: 25, endSeconds: 100);

        Assert.Equal([10, 20], beforeStart.Select(point => point.Time));
        Assert.Equal([20, 30], afterEnd.Select(point => point.Time));
    }

    [Fact]
    public void GetRangeWithBoundaryNeighbors_ReturnsEmpty_ForEmptyIndexOrInvalidRange()
    {
        var empty = new TrackPointTimeIndex([]);
        Assert.Empty(empty.GetRangeWithBoundaryNeighbors(0, 1));

        var index = new TrackPointTimeIndex([Point(1), Point(2)]);
        Assert.Empty(index.GetRangeWithBoundaryNeighbors(double.NaN, 2));
        Assert.Empty(index.GetRangeWithBoundaryNeighbors(2, double.PositiveInfinity));
        Assert.Empty(index.GetRangeWithBoundaryNeighbors(2, 1));
    }

    private static TrackPoint Point(double time) => new(time, x: time, y: time, elevation: null);
}
