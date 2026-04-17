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

    private static TelemetryData CreateTelemetryWithoutStrokes()
    {
        var telemetry = TestTelemetryData.Create(frontPresent: true, rearPresent: true);

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