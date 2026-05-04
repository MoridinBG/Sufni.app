using System;
using System.Diagnostics;
using Avalonia;
using Sufni.App.Plots;

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

    protected override void CreatePlot()
    {
        Debug.Assert(HasPlotControl);

        SetPlotModel(new ImuPlot(PlotControl.Plot));

        if (VelocityPlotView?.IsPlotReady == true)
        {
            LinkVelocity();
        }
        else if (VelocityPlotView != null)
        {
            VelocityPlotView.Loaded += (_, _) => LinkVelocity();
        }

        if (TravelPlotView?.IsPlotReady == true)
        {
            LinkTravel();
        }
        else if (TravelPlotView != null)
        {
            TravelPlotView.Loaded += (_, _) => LinkTravel();
        }

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
    }

    private void LinkVelocity()
    {
        if (VelocityPlotView is null) return;
        LinkXAxisWith(VelocityPlotView);
    }

    private void LinkTravel()
    {
        if (TravelPlotView is null) return;
        LinkXAxisWith(TravelPlotView);
    }
}
