using System;
using System.Diagnostics;
using Avalonia;
using Sufni.App.Plots;

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

    protected override void CreatePlot()
    {
        Debug.Assert(TravelPlotView.IsPlotReady);

        SetPlotModel(new VelocityPlot(PlotControl.Plot));
        LinkXAxisWith(TravelPlotView);

        void UpdateCursor(Avalonia.Input.PointerEventArgs args)
        {
            var point = args.GetPosition(PlotControl);
            var coords = PlotControl.Plot.GetCoordinates((float)point.X, (float)point.Y);
            if (Telemetry is null || Telemetry.Metadata.Duration <= 0) return;

            var normalizedCursorPosition = Math.Clamp(coords.X / Telemetry.Metadata.Duration, 0.0, 1.0);
            Timeline?.SetCursorPosition(normalizedCursorPosition);

            SetCursorPositionWithReadout(coords.X);
        }

        PlotControl.PointerPressed += (_, args) => UpdateCursor(args);
        PlotControl.PointerMoved += (_, args) => UpdateCursor(args);
        PlotControl.PointerExited += (_, _) => HideCursorReadout();

        PlotControl.PointerReleased += (_, _) => UpdateTimelineRange();
        PlotControl.PointerWheelChanged += (_, _) => UpdateTimelineRange();
    }
}
