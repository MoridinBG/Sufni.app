using ScottPlot;
using ScottPlot.Plottables;
using Sufni.Telemetry;
using Sufni.App.MapsAndTracks.Models;

using Sufni.App.MapsAndTracks.Plots;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.MapsAndTracks.Plots;

public class TrackSignalPlotTests
{
    [Fact]
    public void LoadTrackData_SpeedPlot_RendersMetersPerSecondAsKilometersPerHour()
    {
        var plot = new Plot();
        var sut = new TrackSignalPlot(plot);

        sut.LoadTrackData(
            [
                new TrackPoint(100, 0, 0, 500, 10),
                new TrackPoint(101, 1, 1, 501, 20),
            ],
            new TrackTimeRange(100, 1),
            telemetryData: null,
            TrackSignalKind.Speed);

        var trace = Assert.Single(plot.PlottableList.OfType<Scatter>());
        Assert.Equal([0.0, 1.0], trace.Data.GetScatterPoints().Select(point => point.X));
        Assert.Equal([36.0, 72.0], trace.Data.GetScatterPoints().Select(point => point.Y));
    }

    [Fact]
    public void LoadTrackData_ElevationPlot_RendersMeters()
    {
        var plot = new Plot();
        var sut = new TrackSignalPlot(plot);

        sut.LoadTrackData(
            [
                new TrackPoint(100, 0, 0, 500, 10),
                new TrackPoint(101, 1, 1, 510, 20),
            ],
            new TrackTimeRange(100, 1),
            telemetryData: null,
            TrackSignalKind.Elevation);

        var trace = Assert.Single(plot.PlottableList.OfType<Scatter>());
        Assert.Equal([0.0, 1.0], trace.Data.GetScatterPoints().Select(point => point.X));
        Assert.Equal([500.0, 510.0], trace.Data.GetScatterPoints().Select(point => point.Y));
    }

    [Fact]
    public void LoadTrackData_KeepsFiniteSamplesOutsideVisibleDuration()
    {
        var plot = new Plot();
        var sut = new TrackSignalPlot(plot);

        sut.LoadTrackData(
            [
                new TrackPoint(99, 0, 0, 490, 8),
                new TrackPoint(100.5, 1, 1, 500, 10),
                new TrackPoint(102, 2, 2, 510, 12),
            ],
            new TrackTimeRange(100, 1),
            telemetryData: null,
            TrackSignalKind.Elevation);

        var trace = Assert.Single(plot.PlottableList.OfType<Scatter>());
        Assert.Equal([-1.0, 0.5, 2.0], trace.Data.GetScatterPoints().Select(point => point.X));
        Assert.Equal([490.0, 500.0, 510.0], trace.Data.GetScatterPoints().Select(point => point.Y));
        var limits = plot.Axes.GetLimits();
        Assert.Equal(0, limits.Left);
        Assert.Equal(1, limits.Right);
    }

    [Fact]
    public void LoadTrackData_AddsRecordedMarkerLines()
    {
        var plot = new Plot();
        var sut = new TrackSignalPlot(plot);
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry = telemetry with { Metadata = telemetry.Metadata with { Duration = 1 } };
        telemetry = telemetry with { Markers = [new MarkerData(0.5), new MarkerData(0.75)] };

        sut.LoadTrackData(
            [
                new TrackPoint(100, 0, 0, 500, 10),
                new TrackPoint(101, 1, 1, 510, 20),
            ],
            new TrackTimeRange(100, 1),
            telemetry,
            TrackSignalKind.Speed);

        var markerLines = plot.PlottableList
            .OfType<VerticalLine>()
            .Where(line => !double.IsNaN(line.Position))
            .ToArray();

        Assert.Equal(2, markerLines.Length);
        Assert.Contains(markerLines, line => line.Position == 0.5);
        Assert.Contains(markerLines, line => line.Position == 0.75);
    }
}
