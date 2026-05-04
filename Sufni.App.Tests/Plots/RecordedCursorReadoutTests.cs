using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Models;
using Sufni.App.Plots;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.Plots;

public class RecordedCursorReadoutTests
{
    [Fact]
    public void TravelPlot_SetCursorPositionWithReadout_ShowsFrontAndRearTravel()
    {
        var plot = new Plot();
        var sut = new TravelPlot(plot);
        sut.LoadTelemetryData(CreateTelemetryData());

        sut.SetCursorPositionWithReadout(0.5);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("0.5 s", tooltip.LabelText);
        Assert.Contains("Front: 25 mm", tooltip.LabelText);
        Assert.Contains("Rear: 20 mm", tooltip.LabelText);
    }

    [Fact]
    public void TravelPlot_SetCursorPositionWithReadout_UsesNearestSampleNearEnd()
    {
        var plot = new Plot();
        var sut = new TravelPlot(plot);
        sut.LoadTelemetryData(CreateTelemetryData());

        sut.SetCursorPositionWithReadout(1.5);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Front: 75 mm", tooltip.LabelText);
        Assert.Contains("Rear: 60 mm", tooltip.LabelText);
    }

    [Fact]
    public void VelocityPlot_SetCursorPositionWithReadout_ShowsFrontAndRearVelocity()
    {
        var plot = new Plot();
        var sut = new VelocityPlot(plot);
        sut.LoadTelemetryData(CreateTelemetryData());

        sut.SetCursorPositionWithReadout(0.5);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Front: -0.05 m/s", tooltip.LabelText);
        Assert.Contains("Rear: -0.04 m/s", tooltip.LabelText);
    }

    [Fact]
    public void ImuPlot_SetCursorPositionWithReadout_ShowsActiveLocationMagnitudes()
    {
        var plot = new Plot();
        var sut = new ImuPlot(plot);
        sut.LoadTelemetryData(CreateTelemetryDataWithImu());

        sut.SetCursorPositionWithReadout(0);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Frame: 1 g", tooltip.LabelText);
        Assert.Contains("Fork: 2 g", tooltip.LabelText);
    }

    [Fact]
    public void TrackSignalPlot_SetCursorPositionWithReadout_ShowsTrackSignalValue()
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
            TrackSignalKind.Speed);

        sut.SetCursorPositionWithReadout(0);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Speed: 36 km/h", tooltip.LabelText);
    }

    [Fact]
    public void SetCursorPosition_HidesExistingReadout()
    {
        var plot = new Plot();
        var sut = new TravelPlot(plot);
        sut.LoadTelemetryData(CreateTelemetryData());
        sut.SetCursorPositionWithReadout(0.5);
        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());

        sut.SetCursorPosition(1.0);

        Assert.False(tooltip.IsVisible);
    }

    [Fact]
    public void SetCursorPositionWithReadout_DoesNotShowReadout_WhenPlotHasNoData()
    {
        var plot = new Plot();
        var sut = new ImuPlot(plot);
        sut.LoadTelemetryData(CreateTelemetryData());

        sut.SetCursorPositionWithReadout(0.5);

        Assert.Empty(plot.PlottableList.OfType<Tooltip>());
    }
}
