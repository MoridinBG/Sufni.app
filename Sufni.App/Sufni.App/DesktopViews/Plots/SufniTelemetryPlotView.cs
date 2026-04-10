using System;
using Avalonia;
using Sufni.App.Plots;
using Sufni.App.Views;
using Sufni.App.Views.Plots;
using Sufni.Telemetry;

namespace Sufni.App.DesktopViews.Plots;

public abstract class SufniTelemetryPlotView : SufniPlotView
{
    public TelemetryPlot? Plot;
    
    public static readonly StyledProperty<TelemetryData?> TelemetryProperty =
        AvaloniaProperty.Register<SufniTelemetryPlotView, TelemetryData?>(nameof(Telemetry));
    
    public TelemetryData? Telemetry
    {
        get => GetValue(TelemetryProperty);
        set => SetValue(TelemetryProperty, value);
    }
    
    public static readonly StyledProperty<MapView> MapViewProperty =
        AvaloniaProperty.Register<SufniTelemetryPlotView, MapView>(nameof(MapView));

    public MapView MapView
    {
        get => GetValue(MapViewProperty);
        set => SetValue(MapViewProperty, value);
    }
    
    protected SufniTelemetryPlotView()
    {
        // Populate the ScottPlot plot when the Telemetry property is set.
        PropertyChanged += (_, e) =>
        {
            if (e.NewValue is null || AvaPlot is null || Plot is null) return;

            switch (e.Property.Name)
            {
                case nameof(Telemetry):
                    Plot.Plot.Clear();
                    Plot.LoadTelemetryData((TelemetryData)e.NewValue);
                    break;
            }

            AvaPlot.Refresh();
        };

        // Subscribe to map viewport changes for reverse-linking (map → plots)
        PropertyChanged += (_, e) =>
        {
            if (e.Property.Name != nameof(MapView)) return;
            if (e.OldValue is MapView oldMapView)
                oldMapView.ViewportNormalizedRangeChanged -= OnMapViewportRangeChanged;
            if (e.NewValue is MapView newMapView)
                newMapView.ViewportNormalizedRangeChanged += OnMapViewportRangeChanged;
        };
    }

    private void OnMapViewportRangeChanged(double startNormalized, double endNormalized)
    {
        if (AvaPlot is null || Telemetry is null) return;

        var duration = Telemetry.Metadata.Duration;
        if (duration <= 0) return;

        AvaPlot.Plot.Axes.SetLimitsX(startNormalized * duration, endNormalized * duration);
        AvaPlot.Refresh();
    }

    protected void UpdateMapZoom()
    {
        if (AvaPlot is null || Telemetry is null || MapView is null) return;

        var limits = AvaPlot.Plot.Axes.GetLimits();
        var duration = Telemetry.Metadata.Duration;
        if (duration <= 0) return;

        var startNormalized = Math.Clamp(limits.Left / duration, 0.0, 1.0);
        var endNormalized = Math.Clamp(limits.Right / duration, 0.0, 1.0);

        MapView.ZoomToNormalizedRange(startNormalized, endNormalized);
    }
}