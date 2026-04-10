using System;
using System.Diagnostics;
using Avalonia;
using Sufni.App.Plots;

namespace Sufni.App.DesktopViews.Plots;

public class TravelPlotDesktopView : SufniTelemetryPlotView
{
    public static readonly StyledProperty<SufniTelemetryPlotView> VelocityPlotViewProperty =
        AvaloniaProperty.Register<TravelPlotDesktopView, SufniTelemetryPlotView>(nameof(VelocityPlotView));

    public SufniTelemetryPlotView VelocityPlotView
    {
        get => GetValue(VelocityPlotViewProperty);
        set => SetValue(VelocityPlotViewProperty, value);
    }

    public static readonly StyledProperty<SufniTelemetryPlotView> ImuPlotViewProperty =
        AvaloniaProperty.Register<TravelPlotDesktopView, SufniTelemetryPlotView>(nameof(ImuPlotView));

    public SufniTelemetryPlotView ImuPlotView
    {
        get => GetValue(ImuPlotViewProperty);
        set => SetValue(ImuPlotViewProperty, value);
    }

    protected override void CreatePlot()
    {
        Debug.Assert(AvaPlot != null, nameof(AvaPlot) + " != null");
        Plot = new TravelPlot(AvaPlot.Plot);

        AvaPlot.PointerMoved += (_, args) =>
        {
            var point = args.GetPosition(AvaPlot);
            var coords = AvaPlot.Plot.GetCoordinates((float)point.X, (float)point.Y);
            if (Telemetry is null || Telemetry.Metadata.Duration <= 0 || MapView is null) return;

            var normalizedCursorPosition = Math.Clamp(coords.X / Telemetry.Metadata.Duration, 0.0, 1.0);
            MapView.SetNormalizedCursorPosition(normalizedCursorPosition);

            if (Plot is TravelPlot travelPlot)
            {
                travelPlot.CursorLine!.Position = coords.X;
                Plot.Plot.PlotControl!.Refresh();
            }

            if (VelocityPlotView.Plot is VelocityPlot velocityPlot)
            {
                velocityPlot.CursorLine!.Position = coords.X;
                velocityPlot.Plot.PlotControl!.Refresh();
            }

            if (ImuPlotView?.Plot is ImuPlot imuPlot)
            {
                imuPlot.CursorLine!.Position = coords.X;
                imuPlot.Plot.PlotControl!.Refresh();
            }
        };

        AvaPlot.PointerReleased += (_, _) => UpdateMapZoom();
        AvaPlot.PointerWheelChanged += (_, _) => UpdateMapZoom();
    }
}