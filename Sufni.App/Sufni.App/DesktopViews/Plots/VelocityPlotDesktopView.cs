using System;
using System.Diagnostics;
using Avalonia;
using Sufni.App.Plots;
using Sufni.App.ViewModels.Items;
using Sufni.App.Views;

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
    
    public static readonly StyledProperty<SufniTelemetryPlotView> ImuPlotViewProperty =
        AvaloniaProperty.Register<VelocityPlotDesktopView, SufniTelemetryPlotView>(nameof(ImuPlotView));

    public SufniTelemetryPlotView ImuPlotView
    {
        get => GetValue(ImuPlotViewProperty);
        set => SetValue(ImuPlotViewProperty, value);
    }

    public static readonly StyledProperty<MapView> MapViewProperty =
        AvaloniaProperty.Register<VelocityPlotDesktopView, MapView>(nameof(MapView));

    public MapView MapView
    {
        get => GetValue(MapViewProperty);
        set => SetValue(MapViewProperty, value);
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
            if (DataContext is not SessionViewModel { TelemetryData: not null } vm) return;
            
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
            
            if (ImuPlotView?.Plot is ImuPlot imuPlot)
            {
                imuPlot.CursorLine!.Position = coords.X;
                imuPlot.Plot.PlotControl!.Refresh();
            }
        };
    }
}