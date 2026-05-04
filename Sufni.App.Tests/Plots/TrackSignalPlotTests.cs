using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Models;
using Sufni.App.Plots;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Plots;

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

        Assert.NotNull(sut.CursorLine);
        Assert.Equal("Speed", plot.Axes.Title.Label.Text);
        Assert.Single(plot.PlottableList.OfType<Scatter>());
        Assert.True(plot.Axes.Left.Min < 36);
        Assert.True(plot.Axes.Left.Max > 72);
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

        Assert.NotNull(sut.CursorLine);
        Assert.Equal("Elevation", plot.Axes.Title.Label.Text);
        Assert.Single(plot.PlottableList.OfType<Scatter>());
        Assert.True(plot.Axes.Left.Min < 500);
        Assert.True(plot.Axes.Left.Max > 510);
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

        Assert.Single(plot.PlottableList.OfType<Scatter>());
        var limits = plot.Axes.GetLimits();
        Assert.Equal(0, limits.Left);
        Assert.Equal(1, limits.Right);
    }

    [Fact]
    public void LoadTrackData_AddsRecordedMarkerLines()
    {
        var plot = new Plot();
        var sut = new TrackSignalPlot(plot);
        var telemetry = TestTelemetryData.Create();
        telemetry.Metadata.Duration = 1;
        telemetry.Markers = [new MarkerData(0.5), new MarkerData(0.75)];

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
