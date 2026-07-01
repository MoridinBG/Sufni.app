using Sufni.App.ExtensionHost.Contracts.Models;

using Sufni.App.MapsAndTracks.Views;
namespace Sufni.App.Tests.MapsAndTracks.Views;

public class MapViewportControllerTests
{
    private static TrackPoint Point(double x, double y) => new(0, x, y, null, null, null, null, null, null);

    [Fact]
    public void ComputeRangeFit_ReturnsNull_ForEmptyRange()
    {
        Assert.Null(MapViewportController.ComputeRangeFit([], padding: 0.1));
    }

    [Fact]
    public void ComputeRangeFit_PointLikeWindow_CentersWithClampedResolution()
    {
        var fit = MapViewportController.ComputeRangeFit([Point(10, 20), Point(10, 20)], padding: 0.1);

        var center = Assert.IsType<MapViewportController.CenterFit>(fit);
        Assert.Equal(10, center.X);
        Assert.Equal(20, center.Y);
        Assert.Equal(10, center.MaxResolution);
    }

    [Fact]
    public void ComputeRangeFit_AppliesPaddingToRegularExtent()
    {
        var fit = MapViewportController.ComputeRangeFit([Point(0, 0), Point(100, 50)], padding: 0.1);

        var extent = Assert.IsType<MapViewportController.ExtentFit>(fit);
        Assert.Equal(-10, extent.MinX);
        Assert.Equal(110, extent.MaxX);
        Assert.Equal(-5, extent.MinY);
        Assert.Equal(55, extent.MaxY);
    }

    [Fact]
    public void ComputeRangeFit_ExpandsVerticalLineToNonZeroWidth()
    {
        var fit = MapViewportController.ComputeRangeFit([Point(10, 0), Point(10, 100)], padding: 0);

        var extent = Assert.IsType<MapViewportController.ExtentFit>(fit);
        Assert.Equal(-40, extent.MinX);
        Assert.Equal(60, extent.MaxX);
        Assert.Equal(0, extent.MinY);
        Assert.Equal(100, extent.MaxY);
    }

    [Fact]
    public void ComputeRangeFit_ExpandsHorizontalLineToNonZeroHeight()
    {
        var fit = MapViewportController.ComputeRangeFit([Point(0, 10), Point(100, 10)], padding: 0);

        var extent = Assert.IsType<MapViewportController.ExtentFit>(fit);
        Assert.Equal(0, extent.MinX);
        Assert.Equal(100, extent.MaxX);
        Assert.Equal(-40, extent.MinY);
        Assert.Equal(60, extent.MaxY);
    }

    [Fact]
    public void ComputeBounds_ProjectsViewportToWorldUnits()
    {
        var bounds = MapViewportController.ComputeBounds(
            centerX: 100,
            centerY: 200,
            width: 800,
            height: 600,
            resolution: 0.5);

        Assert.Equal(-100, bounds.MinX);
        Assert.Equal(300, bounds.MaxX);
        Assert.Equal(50, bounds.MinY);
        Assert.Equal(350, bounds.MaxY);
    }
}
