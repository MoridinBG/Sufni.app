using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ScottPlot.Plottables;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Plots;
using Sufni.App.Tests.Infrastructure;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.Views;

public class DesktopTelemetryPlotViewTests
{
    [AvaloniaFact]
    public async Task ImuPlotDesktopView_LoadsSignalsFromTelemetryProperty()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new ImuPlotDesktopView();
        var host = new Window
        {
            Width = 900,
            Height = 700,
            Content = view
        };

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        view.Telemetry = CreateTelemetryDataWithImu();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = Assert.IsType<ImuPlot>(view.Plot);
        Assert.NotNull(view.AvaPlot);
        Assert.NotNull(plot.CursorLine);
        Assert.Equal(2, plot.Plot.PlottableList.OfType<Signal>().Count());

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task TravelPlotDesktopView_LoadsSignalsFromTelemetryProperty()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new TravelPlotDesktopView();
        var host = new Window
        {
            Width = 900,
            Height = 700,
            Content = view
        };

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        view.Telemetry = CreateTelemetryData();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = Assert.IsType<TravelPlot>(view.Plot);
        Assert.NotNull(view.AvaPlot);
        Assert.NotNull(plot.CursorLine);
        Assert.Equal(2, plot.Plot.PlottableList.OfType<Signal>().Count());

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task VelocityPlotDesktopView_LoadsSignalsFromTelemetryProperty()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var travelView = new TravelPlotDesktopView();
        var travelHost = new Window
        {
            Width = 900,
            Height = 700,
            Content = travelView
        };

        travelHost.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        travelView.Telemetry = CreateTelemetryData();
        await ViewTestHelpers.FlushDispatcherAsync();

        var velocityView = new VelocityPlotDesktopView
        {
            TravelPlotView = travelView
        };
        var velocityHost = new Window
        {
            Width = 900,
            Height = 700,
            Content = velocityView
        };

        velocityHost.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        velocityView.Telemetry = CreateTelemetryData();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = Assert.IsType<VelocityPlot>(velocityView.Plot);
        Assert.NotNull(velocityView.AvaPlot);
        Assert.NotNull(plot.CursorLine);
        Assert.Equal(2, plot.Plot.PlottableList.OfType<Signal>().Count());

        velocityHost.Close();
        travelHost.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}