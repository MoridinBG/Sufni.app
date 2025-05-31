using System.Diagnostics;
using Avalonia;
using Sufni.App.Plots;

namespace Sufni.App.DesktopViews.Plots;

public class VelocityPlotDesktopView : SufniTelemetryPlotView
{
    public static readonly StyledProperty<SufniTelemetryPlotView> TravelPlotProperty =
        AvaloniaProperty.Register<VelocityPlotDesktopView, SufniTelemetryPlotView>(nameof(TravelPlot));

    public SufniTelemetryPlotView TravelPlot
    {
        get => GetValue(TravelPlotProperty);
        set => SetValue(TravelPlotProperty, value);
    }

    protected override void CreatePlot()
    {
        Debug.Assert(AvaPlot != null);
        Debug.Assert(TravelPlot.AvaPlot != null);
        Plot = new VelocityPlot(AvaPlot.Plot);
        Plot.Plot.Axes.Link(TravelPlot.AvaPlot, x: true, y: false);
        TravelPlot.AvaPlot.Plot.Axes.Link(Plot.Plot, x: true, y: false);
    }
}