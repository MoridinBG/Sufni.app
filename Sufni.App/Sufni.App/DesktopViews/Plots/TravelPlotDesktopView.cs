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
        SetPlotModel(new TravelPlot(PlotControl.Plot));

        void UpdateCursor(Avalonia.Input.PointerEventArgs args)
        {
            var point = args.GetPosition(PlotControl);
            var coords = PlotControl.Plot.GetCoordinates((float)point.X, (float)point.Y);
            if (Telemetry is null || Telemetry.Metadata.Duration <= 0) return;

            var normalizedCursorPosition = Math.Clamp(coords.X / Telemetry.Metadata.Duration, 0.0, 1.0);
            Timeline?.SetCursorPosition(normalizedCursorPosition);

            SetCursorPosition(coords.X);
            VelocityPlotView?.SetCursorPosition(coords.X);
            ImuPlotView?.SetCursorPosition(coords.X);
        }

        PlotControl.PointerPressed += (_, args) => UpdateCursor(args);
        PlotControl.PointerMoved += (_, args) => UpdateCursor(args);

        PlotControl.PointerReleased += (_, _) => UpdateTimelineRange();
        PlotControl.PointerWheelChanged += (_, _) => UpdateTimelineRange();
    }
}