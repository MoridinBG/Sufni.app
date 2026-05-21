using Mapsui.Projections;
using Sufni.App.Models;

namespace Sufni.App.Tests.Models;

public class SessionSummaryMetricsCalculatorTests
{
    [Fact]
    public void Calculate_ReturnsDurationOnly_WhenTrackPointsAreMissing()
    {
        var metrics = SessionSummaryMetricsCalculator.Calculate(65.4, points: null);

        Assert.Equal(65.4, metrics.DurationSeconds);
        Assert.Null(metrics.DistanceMeters);
        Assert.Null(metrics.AscentMeters);
        Assert.Null(metrics.DescentMeters);
    }

    [Fact]
    public void Calculate_SumsDistanceAscentAndDescent_FromFiniteProjectedPoints()
    {
        var points = new List<TrackPoint>
        {
            new(time: 0, x: 0, y: 0, elevation: 10),
            new(time: 1, x: 3, y: 4, elevation: 15),
            new(time: 2, x: 6, y: 8, elevation: 12),
        };

        var metrics = SessionSummaryMetricsCalculator.Calculate(120, points);

        Assert.Equal(120, metrics.DurationSeconds);
        Assert.InRange(metrics.DistanceMeters!.Value, 9.98, 10.0);
        Assert.Equal(5, metrics.AscentMeters);
        Assert.Equal(3, metrics.DescentMeters);
    }

    [Fact]
    public void Calculate_UsesGroundDistance_NotMercatorPlaneDistance()
    {
        var first = CreateProjectedPoint(time: 0, longitude: 10.0, latitude: 45.0, elevation: 100);
        var second = CreateProjectedPoint(time: 1, longitude: 10.01, latitude: 45.0, elevation: 103);
        var mercatorDeltaX = Math.Abs(second.X - first.X);

        var metrics = SessionSummaryMetricsCalculator.Calculate(10, [first, second]);

        Assert.InRange(metrics.DistanceMeters!.Value, 785, 787);
        Assert.InRange(mercatorDeltaX, 1110, 1115);
    }

    [Fact]
    public void Calculate_IgnoresInvalidProjectedPoints()
    {
        var points = new List<TrackPoint>
        {
            new(time: 0, x: double.NaN, y: 0, elevation: 100),
            new(time: 1, x: 0, y: 0, elevation: 10),
            new(time: 2, x: 0, y: 3, elevation: 14),
            new(time: 3, x: double.PositiveInfinity, y: 5, elevation: 50),
        };

        var metrics = SessionSummaryMetricsCalculator.Calculate(double.PositiveInfinity, points);

        Assert.Null(metrics.DurationSeconds);
        Assert.InRange(metrics.DistanceMeters!.Value, 2.99, 3.0);
        Assert.Equal(4, metrics.AscentMeters);
        Assert.Equal(0, metrics.DescentMeters);
    }

    [Fact]
    public void Calculate_IgnoresElevationNoise_BelowThreshold()
    {
        var points = new List<TrackPoint>
        {
            new(time: 0, x: 0, y: 0, elevation: 100),
            new(time: 1, x: 1, y: 0, elevation: 101.4),
            new(time: 2, x: 2, y: 0, elevation: 99.2),
            new(time: 3, x: 3, y: 0, elevation: 102.5),
        };

        var metrics = SessionSummaryMetricsCalculator.Calculate(10, points);

        Assert.Equal(0, metrics.AscentMeters);
        Assert.Equal(0, metrics.DescentMeters);
    }

    [Fact]
    public void Calculate_OmitsElevationGain_WhenLessThanTwoFiniteElevationSamplesExist()
    {
        var points = new List<TrackPoint>
        {
            new(time: 0, x: 0, y: 0, elevation: null),
            new(time: 1, x: 1, y: 0, elevation: 10),
            new(time: 2, x: 2, y: 0, elevation: double.NaN),
        };

        var metrics = SessionSummaryMetricsCalculator.Calculate(10, points);

        Assert.InRange(metrics.DistanceMeters!.Value, 1.99, 2.0);
        Assert.Null(metrics.AscentMeters);
        Assert.Null(metrics.DescentMeters);
    }

    private static TrackPoint CreateProjectedPoint(double time, double longitude, double latitude, double? elevation)
    {
        var (x, y) = SphericalMercator.FromLonLat(longitude, latitude);
        return new TrackPoint(time, x, y, elevation);
    }
}
