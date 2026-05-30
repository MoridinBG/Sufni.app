using ScottPlot;
using Sufni.App.Plots;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Plots;

public class TravelHistogramPlotTests
{
    [Fact]
    public void LoadTelemetryData_WithoutStrokeData_SkipsRendering()
    {
        var telemetry = CreateTelemetryWithoutStrokes();
        var plot = new Plot();
        var sut = new TravelHistogramPlot(plot, SuspensionType.Front);

        sut.LoadTelemetryData(telemetry);

        Assert.Empty(plot.PlottableList);
        Assert.True(string.IsNullOrWhiteSpace(plot.Axes.Title.Label.Text));
    }

    [Fact]
    public void LoadTelemetryData_WithDynamicSagMode_RendersWithoutStrokeData()
    {
        var telemetry = CreateTelemetryWithoutStrokes();
        var plot = new Plot();
        var sut = new TravelHistogramPlot(plot, SuspensionType.Front)
        {
            HistogramMode = TravelHistogramMode.DynamicSag,
        };

        sut.LoadTelemetryData(telemetry);

        Assert.NotEmpty(plot.PlottableList);
        Assert.Equal("Front travel", plot.Axes.Title.Label.Text);
    }

    [Fact]
    public void Clear_RemovesAxisRulesBeforeReloadingChangedTravelRange()
    {
        var original = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);
        var recomputed = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);
        recomputed.Front.MaxTravel = 240;
        recomputed.Front.TravelBins = HistogramBuilder.Linspace(0, 240, Parameters.TravelHistBins + 1);
        var plot = new Plot();
        var sut = new TravelHistogramPlot(plot, SuspensionType.Front);

        sut.LoadTelemetryData(original);
        Assert.Equal(2, plot.Axes.Rules.Count);

        sut.Clear();
        Assert.Empty(plot.Axes.Rules);

        sut.LoadTelemetryData(recomputed);

        Assert.Equal(2, plot.Axes.Rules.Count);
        Assert.True(plot.Axes.GetLimits().Bottom > 230);
    }

    private static TelemetryData CreateTelemetryWithoutStrokes()
    {
        var telemetry = TestTelemetryData.CreateProcessed(frontPresent: true, rearPresent: true);

        telemetry.Front.Strokes = new Strokes
        {
            Compressions = [],
            Rebounds = [],
        };

        telemetry.Rear.Strokes = new Strokes
        {
            Compressions = [],
            Rebounds = [],
        };

        return telemetry;
    }
}
