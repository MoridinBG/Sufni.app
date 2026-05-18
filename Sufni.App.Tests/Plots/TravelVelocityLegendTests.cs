using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Plots;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.Plots;

public class TravelVelocityLegendTests
{
    [Fact]
    public void TravelPlot_LoadTelemetryData_ShowsLegend_WhenOnlyFrontSourceIsPresent()
    {
        var telemetry = CreateTelemetryData();
        telemetry.Rear.Present = false;
        var plot = new Plot();
        var sut = new TravelPlot(plot);

        sut.LoadTelemetryData(telemetry);

        Assert.True(plot.Legend.IsVisible);
        Assert.Equal(
            ["Front"],
            plot.PlottableList.OfType<Signal>().Select(signal => signal.LegendText).ToArray());
    }

    [Fact]
    public void VelocityPlot_LoadTelemetryData_ShowsLegend_WhenOnlyRearSourceIsPresent()
    {
        var telemetry = CreateTelemetryData();
        telemetry.Front.Present = false;
        var plot = new Plot();
        var sut = new VelocityPlot(plot);

        sut.LoadTelemetryData(telemetry);

        Assert.True(plot.Legend.IsVisible);
        Assert.Equal(
            ["Rear"],
            plot.PlottableList.OfType<Signal>().Select(signal => signal.LegendText).ToArray());
    }
}
