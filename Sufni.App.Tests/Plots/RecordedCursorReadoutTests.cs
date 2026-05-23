using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Models;
using Sufni.App.Plots;
using static Sufni.App.Tests.Infrastructure.TestTelemetryData;

namespace Sufni.App.Tests.Plots;

public class RecordedCursorReadoutTests
{
    [Fact]
    public void TravelPlot_SetCursorPositionWithReadout_ShowsFrontAndRearTravel()
    {
        var plot = new Plot();
        var sut = new TravelPlot(plot);
        sut.LoadTelemetryData(CreateMinimal());

        sut.SetCursorPositionWithReadout(0.5);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("0.5 s", tooltip.LabelText);
        Assert.Contains("Front: 25 mm", tooltip.LabelText);
        Assert.Contains("Rear: 20 mm", tooltip.LabelText);
    }

    [Fact]
    public void TravelPlot_SetCursorPositionWithReadout_ExcludesHiddenSources()
    {
        var visibility = new TelemetrySourceVisibilityStore();
        visibility.SetVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Rear, visible: false);
        var plot = new Plot();
        var sut = new TravelPlot(plot)
        {
            SourceVisibility = visibility,
        };
        sut.LoadTelemetryData(CreateMinimal());

        sut.SetCursorPositionWithReadout(0.5);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Front: 25 mm", tooltip.LabelText);
        Assert.DoesNotContain("Rear:", tooltip.LabelText);
    }

    [Fact]
    public void TravelPlot_SetCursorPositionWithReadout_UsesNearestSampleNearEnd()
    {
        var plot = new Plot();
        var sut = new TravelPlot(plot);
        sut.LoadTelemetryData(CreateMinimal());

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
        sut.LoadTelemetryData(CreateMinimal());

        sut.SetCursorPositionWithReadout(0.5);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Front: -0.05 m/s", tooltip.LabelText);
        Assert.Contains("Rear: -0.04 m/s", tooltip.LabelText);
    }

    [Fact]
    public void ImuPlot_SetCursorPositionWithReadout_ShowsActiveLocationVibrationRms()
    {
        var plot = new Plot();
        var sut = new ImuPlot(plot);
        sut.LoadTelemetryData(CreateWithImu());

        sut.SetCursorPositionWithReadout(0);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Frame: 0.414 g", tooltip.LabelText);
        Assert.Contains("Fork: 1.236 g", tooltip.LabelText);
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
    public void TrackSignalPlot_SetCursorPositionWithReadout_ShowsGpsQuality_ForElevation()
    {
        var plot = new Plot();
        var sut = new TrackSignalPlot(plot);
        sut.LoadTrackData(
            [
                new TrackPoint(100, 0, 0, 500, 10, fixMode: 3, satellites: 12, epe2d: 0.5f, epe3d: 0.8f),
                new TrackPoint(101, 1, 1, 510, 20, fixMode: 3, satellites: 11, epe2d: 0.6f, epe3d: 0.9f),
            ],
            new TrackTimeRange(100, 1),
            telemetryData: null,
            TrackSignalKind.Elevation);

        sut.SetCursorPositionWithReadout(0);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Elevation: 500 m", tooltip.LabelText);
        Assert.Contains("Satellites: 12", tooltip.LabelText);
        Assert.Contains("EPE 2D: 0.5 m", tooltip.LabelText);
        Assert.Contains("EPE 3D: 0.8 m", tooltip.LabelText);
    }

    [Fact]
    public void TrackSignalPlot_SetCursorPositionWithReadout_ShowsNearestGpsQuality_ForInterpolatedElevationPoint()
    {
        var plot = new Plot();
        var sut = new TrackSignalPlot(plot);
        sut.LoadTrackData(
            [
                new TrackPoint(100, 0, 0, 500, 10, fixMode: 3, satellites: 12, epe2d: 0.5f, epe3d: 0.8f),
                new TrackPoint(100.1, 1, 1, 501, 11),
                new TrackPoint(100.2, 2, 2, 502, 12),
                new TrackPoint(101, 3, 3, 510, 20, fixMode: 3, satellites: 9, epe2d: 0.7f, epe3d: 1.1f),
            ],
            new TrackTimeRange(100, 1),
            telemetryData: null,
            TrackSignalKind.Elevation);

        sut.SetCursorPositionWithReadout(0.2);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Elevation: 502 m", tooltip.LabelText);
        Assert.Contains("Satellites: 12", tooltip.LabelText);
        Assert.Contains("EPE 2D: 0.5 m", tooltip.LabelText);
        Assert.Contains("EPE 3D: 0.8 m", tooltip.LabelText);
    }

    [Fact]
    public void TrackSignalPlot_SetCursorPositionWithReadout_DoesNotShowGpsQuality_ForSpeed()
    {
        var plot = new Plot();
        var sut = new TrackSignalPlot(plot);
        sut.LoadTrackData(
            [
                new TrackPoint(100, 0, 0, 500, 10, fixMode: 3, satellites: 12, epe2d: 0.5f, epe3d: 0.8f),
                new TrackPoint(101, 1, 1, 510, 20, fixMode: 3, satellites: 11, epe2d: 0.6f, epe3d: 0.9f),
            ],
            new TrackTimeRange(100, 1),
            telemetryData: null,
            TrackSignalKind.Speed);

        sut.SetCursorPositionWithReadout(0);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Speed: 36 km/h", tooltip.LabelText);
        Assert.DoesNotContain("Satellites", tooltip.LabelText);
        Assert.DoesNotContain("EPE", tooltip.LabelText);
    }

    [Fact]
    public void SetCursorPosition_HidesExistingReadout()
    {
        var plot = new Plot();
        var sut = new TravelPlot(plot);
        sut.LoadTelemetryData(CreateMinimal());
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
        sut.LoadTelemetryData(CreateMinimal());

        sut.SetCursorPositionWithReadout(0.5);

        Assert.Empty(plot.PlottableList.OfType<Tooltip>());
    }
}
