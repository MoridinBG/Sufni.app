using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using ScottPlot.Plottables;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Plots;
using Sufni.App.Views;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.Views;

public class DesktopTelemetryPlotViewTests
{
    [AvaloniaFact]
    public async Task ImuPlotDesktopView_LoadsSignalsFromTelemetryProperty()
    {
        EnsurePlotViewStyle();

        var view = new ImuPlotDesktopView
        {
            MapView = new MapView()
        };
        var host = new Window
        {
            Width = 900,
            Height = 700,
            Content = view
        };

        host.Show();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        view.Telemetry = CreateTelemetryDataWithImu();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        var plot = Assert.IsType<ImuPlot>(view.Plot);
        Assert.NotNull(view.AvaPlot);
        Assert.NotNull(plot.CursorLine);
        Assert.Equal(2, plot.Plot.PlottableList.OfType<Signal>().Count());

        host.Close();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    [AvaloniaFact]
    public async Task TravelPlotDesktopView_LoadsSignalsFromTelemetryProperty()
    {
        EnsurePlotViewStyle();

        var view = new TravelPlotDesktopView
        {
            MapView = new MapView()
        };
        var host = new Window
        {
            Width = 900,
            Height = 700,
            Content = view
        };

        host.Show();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        view.Telemetry = CreateTelemetryData();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        var plot = Assert.IsType<TravelPlot>(view.Plot);
        Assert.NotNull(view.AvaPlot);
        Assert.NotNull(plot.CursorLine);
        Assert.Equal(2, plot.Plot.PlottableList.OfType<Signal>().Count());

        host.Close();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    [AvaloniaFact]
    public async Task VelocityPlotDesktopView_LoadsSignalsFromTelemetryProperty()
    {
        EnsurePlotViewStyle();

        var travelView = new TravelPlotDesktopView
        {
            MapView = new MapView()
        };
        var travelHost = new Window
        {
            Width = 900,
            Height = 700,
            Content = travelView
        };

        travelHost.Show();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        travelView.Telemetry = CreateTelemetryData();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        var velocityView = new VelocityPlotDesktopView
        {
            MapView = new MapView(),
            TravelPlotView = travelView
        };
        var velocityHost = new Window
        {
            Width = 900,
            Height = 700,
            Content = velocityView
        };

        velocityHost.Show();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        velocityView.Telemetry = CreateTelemetryData();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        var plot = Assert.IsType<VelocityPlot>(velocityView.Plot);
        Assert.NotNull(velocityView.AvaPlot);
        Assert.NotNull(plot.CursorLine);
        Assert.Equal(2, plot.Plot.PlottableList.OfType<Signal>().Count());

        velocityHost.Close();
        travelHost.Close();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static void EnsurePlotViewStyle()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");
        var source = new Uri("avares://Sufni.App/Views/Plots/SufniPlotView.axaml");

        if (application.Styles.OfType<StyleInclude>().Any(style => style.Source?.AbsoluteUri == source.AbsoluteUri))
        {
            return;
        }

        application.Styles.Add(new StyleInclude(new Uri("avares://Sufni.App/"))
        {
            Source = source
        });
    }
}