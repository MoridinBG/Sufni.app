using Avalonia.Headless.XUnit;
using ScottPlot.Plottables;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Tests.Infrastructure;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.Views.Plots;

public class TravelPlotDesktopViewTests
{
    [AvaloniaFact]
    public async Task TravelPlotDesktopView_StartsEmpty_BeforeTelemetryIsAssigned()
    {
        var view = new TravelPlotDesktopView();

        Assert.Null(view.MaximumDisplayHz);

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Empty(plot.Plot.PlottableList);
    }

    [AvaloniaFact]
    public async Task TravelPlotDesktopView_LoadsSignalsFromTelemetryProperty()
    {
        var view = new TravelPlotDesktopView();

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateTelemetryData();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Equal("Travel (mm / seconds)", plot.Plot.Axes.Title.Label.Text);
        Assert.Single(plot.Plot.PlottableList.OfType<VerticalLine>());
        Assert.Equal(2, plot.Plot.PlottableList.OfType<Signal>().Count());
    }
}