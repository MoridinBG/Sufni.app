using System;
using System.Diagnostics;
using Avalonia;
using Sufni.App.Plots;
using Sufni.App.ViewModels.Items;
using Sufni.App.Views;

namespace Sufni.App.DesktopViews.Plots;

public class ImuPlotDesktopView : SufniTelemetryPlotView
{
    public static readonly StyledProperty<SufniTelemetryPlotView> VelocityPlotViewProperty =
        AvaloniaProperty.Register<ImuPlotDesktopView, SufniTelemetryPlotView>(nameof(VelocityPlotView));

    public SufniTelemetryPlotView VelocityPlotView
    {
        get => GetValue(VelocityPlotViewProperty);
        set => SetValue(VelocityPlotViewProperty, value);
    }
    
    public static readonly StyledProperty<SufniTelemetryPlotView> TravelPlotViewProperty =
        AvaloniaProperty.Register<ImuPlotDesktopView, SufniTelemetryPlotView>(nameof(TravelPlotView));

    public SufniTelemetryPlotView TravelPlotView
    {
        get => GetValue(TravelPlotViewProperty);
        set => SetValue(TravelPlotViewProperty, value);
    }
    
    public static readonly StyledProperty<MapView> MapViewProperty =
        AvaloniaProperty.Register<ImuPlotDesktopView, MapView>(nameof(MapView));

    public MapView MapView
    {
        get => GetValue(MapViewProperty);
        set => SetValue(MapViewProperty, value);
    }

    protected override void CreatePlot()
    {
        Debug.Assert(AvaPlot != null);

        Plot = new ImuPlot(AvaPlot.Plot);
        
        if (VelocityPlotView?.AvaPlot != null)
        {
            LinkVelocity();
        }
        else if (VelocityPlotView != null)
        {
            VelocityPlotView.Loaded += (_, _) => LinkVelocity();
        }

        if (TravelPlotView?.AvaPlot != null)
        {
            LinkTravel();
        }
        else if (TravelPlotView != null)
        {
            TravelPlotView.Loaded += (_, _) => LinkTravel();
        }

        AvaPlot.PointerMoved += (_, args) =>
        {
            var point = args.GetPosition(AvaPlot);
            var coords = AvaPlot.Plot.GetCoordinates((float)point.X, (float)point.Y);
            if (DataContext is not SessionViewModel { TelemetryData: not null } vm) return;
            
            var normalizedCursorPosition = Math.Clamp(coords.X / vm.TelemetryData.Metadata.Duration, 0.0, 1.0);
            MapView.SetNormalizedCursorPosition(normalizedCursorPosition);

            if (Plot is ImuPlot imuPlot)
            {
                imuPlot.CursorLine!.Position = coords.X;
                Plot.Plot.PlotControl!.Refresh();
            }
            
            if (VelocityPlotView?.Plot is VelocityPlot velocityPlot)
            {
                velocityPlot.CursorLine!.Position = coords.X;
                velocityPlot.Plot.PlotControl!.Refresh();
            }

            if (TravelPlotView?.Plot is TravelPlot travelPlot)
            {
                travelPlot.CursorLine!.Position = coords.X;
                travelPlot.Plot.PlotControl!.Refresh();
            }
        };
    }

    private void LinkVelocity()
    {
        if (VelocityPlotView?.AvaPlot == null || AvaPlot == null) return;
        Plot!.Plot.Axes.Link(VelocityPlotView.AvaPlot, x: true, y: false);
        VelocityPlotView.AvaPlot.Plot.Axes.Link(AvaPlot, x: true, y: false);
    }

    private void LinkTravel()
    {
        if (TravelPlotView?.AvaPlot == null || AvaPlot == null) return;
        Plot!.Plot.Axes.Link(TravelPlotView.AvaPlot, x: true, y: false);
        TravelPlotView.AvaPlot.Plot.Axes.Link(AvaPlot, x: true, y: false);
    }
}
