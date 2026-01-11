using System;
using System.Diagnostics;
using Avalonia;
using Sufni.App.Plots;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.DesktopViews.Plots;

public class VelocityPlotDesktopView : SufniTelemetryPlotView
{
    public static readonly StyledProperty<SufniTelemetryPlotView> TravelPlotViewProperty =
        AvaloniaProperty.Register<VelocityPlotDesktopView, SufniTelemetryPlotView>(nameof(TravelPlotView));

    public SufniTelemetryPlotView TravelPlotView
    {
        get => GetValue(TravelPlotViewProperty);
        set => SetValue(TravelPlotViewProperty, value);
    }

    protected override void CreatePlot()
    {
        Debug.Assert(AvaPlot != null);
        Debug.Assert(TravelPlotView.AvaPlot != null);

        Plot = new VelocityPlot(AvaPlot.Plot);
        Plot.Plot.Axes.Link(TravelPlotView.AvaPlot, x: true, y: false);
        TravelPlotView.AvaPlot.Plot.Axes.Link(Plot.Plot, x: true, y: false);

        AvaPlot.PointerMoved += (_, args) =>
        {
            var point = args.GetPosition(AvaPlot);
            var coords = AvaPlot.Plot.GetCoordinates((float)point.X, (float)point.Y);
            if (DataContext is not SessionViewModel vm) return;

            Debug.Assert(vm.TelemetryData is not null);
            var normalizedCursorPosition = Math.Clamp(coords.X / vm.TelemetryData.Metadata.Duration, 0.0, 1.0);
            MapView.SetNormalizedCursorPosition(normalizedCursorPosition);

            if (Plot is VelocityPlot velocityPlot)
            {
                velocityPlot.CursorLine!.Position = coords.X;
                Plot.Plot.PlotControl!.Refresh();
            }

            if (TravelPlotView.Plot is TravelPlot travelPlot)
            {
                travelPlot.CursorLine!.Position = coords.X;
                travelPlot.Plot.PlotControl!.Refresh();
            }
        };
        
                
        // Update map zoom when plot is zoomed/panned
        AvaPlot.PointerReleased += (_, _) => UpdateMapZoom();
        AvaPlot.PointerWheelChanged += (_, _) => UpdateMapZoom();
    }
}