using Avalonia.Headless.XUnit;
using ScottPlot.Plottables;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Tests.Infrastructure;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.Views.Plots;

public class VelocityPlotDesktopViewTests
{
    [AvaloniaFact]
    public async Task VelocityPlotDesktopView_StartsEmpty_BeforeTelemetryIsAssigned()
    {
        var travelView = new TravelPlotDesktopView();
        await using var mountedTravel = await PlotViewTestSupport.MountAsync(travelView);

        var velocityView = new VelocityPlotDesktopView
        {
            TravelPlotView = mountedTravel.View,
        };

        await using var mountedVelocity = await PlotViewTestSupport.MountAsync(velocityView);

        var plot = PlotViewTestSupport.GetRenderedPlot(mountedVelocity.View);
        Assert.Empty(plot.Plot.PlottableList);
    }

    [AvaloniaFact]
    public async Task VelocityPlotDesktopView_LoadsSignalsFromTelemetryProperty()
    {
        var travelView = new TravelPlotDesktopView();
        await using var mountedTravel = await PlotViewTestSupport.MountAsync(travelView);

        var velocityView = new VelocityPlotDesktopView
        {
            TravelPlotView = mountedTravel.View,
        };

        await using var mountedVelocity = await PlotViewTestSupport.MountAsync(velocityView);

        velocityView.Telemetry = CreateTelemetryData();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mountedVelocity.View);
        Assert.Equal("Velocity (m/seconds / time )", plot.Plot.Axes.Title.Label.Text);
        Assert.Single(plot.Plot.PlottableList.OfType<VerticalLine>());
        Assert.Equal(2, plot.Plot.PlottableList.OfType<Signal>().Count());
    }
}