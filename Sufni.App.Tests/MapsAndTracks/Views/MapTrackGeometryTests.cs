using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

using Sufni.App.MapsAndTracks.Views;
namespace Sufni.App.Tests.MapsAndTracks.Views;

public class MapTrackGeometryTests
{
    [Fact]
    public void FindClosestTrackPoint_IgnoresNonFiniteTimes()
    {
        var points = new[]
        {
            Point(0, 0, 0),
            Point(double.NaN, 1, 1),
            Point(2, 2, 2),
            Point(4, 4, 4),
        };

        var closest = MapTrackGeometry.FindClosestTrackPoint(points, 2.6);

        Assert.Same(points[2], closest);
    }

    [Fact]
    public void GetTrackPointsInTimeRange_IncludesNearestOutsidePoints()
    {
        var points = new[]
        {
            Point(0, 0, 0),
            Point(1, 10, 10),
            Point(double.PositiveInfinity, 15, 15),
            Point(2, 20, 20),
            Point(3, 30, 30),
            Point(4, 40, 40),
        };

        var result = MapTrackGeometry.GetTrackPointsInTimeRange(points, startSeconds: 1.5, endSeconds: 2.5);

        Assert.Equal([1, 2, 3], result.Select(point => point.Time));
    }

    [Fact]
    public void TryGetVisibleTrackRange_UsesIntersectingSegments()
    {
        var points = new[]
        {
            Point(10, -10, 5),
            Point(20, 20, 5),
        };

        var found = MapTrackGeometry.TryGetVisibleTrackRange(
            points,
            new TrackTimeRange(OriginSeconds: 10, DurationSeconds: 10),
            minX: 0,
            maxX: 10,
            minY: 0,
            maxY: 10,
            out var start,
            out var end);

        Assert.True(found);
        Assert.Equal(0, start);
        Assert.Equal(1, end);
    }

    [Fact]
    public void TryGetVisibleTrackRange_ReturnsFalseForSingleVisiblePoint()
    {
        var points = new[]
        {
            Point(10, 5, 5),
        };

        var found = MapTrackGeometry.TryGetVisibleTrackRange(
            points,
            new TrackTimeRange(OriginSeconds: 10, DurationSeconds: 10),
            minX: 0,
            maxX: 10,
            minY: 0,
            maxY: 10,
            out var start,
            out var end);

        Assert.False(found);
        Assert.Equal(0, start);
        Assert.Equal(1, end);
    }

    [Fact]
    public void NormalizeTime_ClampsToTimelineRange()
    {
        var context = new TrackTimeRange(OriginSeconds: 100, DurationSeconds: 50);

        Assert.Equal(0, MapTrackGeometry.NormalizeTime(50, context));
        Assert.Equal(0.5, MapTrackGeometry.NormalizeTime(125, context));
        Assert.Equal(1, MapTrackGeometry.NormalizeTime(200, context));
    }

    [Fact]
    public void SegmentIntersectsViewport_ReturnsTrueForCrossingSegment()
    {
        var intersects = MapTrackGeometry.SegmentIntersectsViewport(
            Point(0, -5, 5),
            Point(1, 15, 5),
            minX: 0,
            maxX: 10,
            minY: 0,
            maxY: 10);

        Assert.True(intersects);
    }

    [Fact]
    public void ClipSegment_ReturnsFalseForParallelSegmentOutsideBoundary()
    {
        var entering = 0d;
        var leaving = 1d;

        var visible = MapTrackGeometry.ClipSegment(0, -1, ref entering, ref leaving);

        Assert.False(visible);
        Assert.Equal(0, entering);
        Assert.Equal(1, leaving);
    }

    [Fact]
    public void ProjectMapCoordinate_ProjectsEquatorOriginToMercatorOrigin()
    {
        var projected = MapTrackGeometry.ProjectMapCoordinate(new RecordedSessionMapCoordinate(0, 0));

        Assert.Equal(0, projected.X, precision: 6);
        Assert.Equal(0, projected.Y, precision: 6);
    }

    private static TrackPoint Point(double time, double x, double y) => new(time, x, y, elevation: null);
}
