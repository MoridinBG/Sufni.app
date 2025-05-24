using Avalonia;
using Sufni.App.Plots;
using Sufni.Telemetry;

namespace Sufni.App.DesktopViews.Plots;

public abstract class SufniTelemetryPlotView : SufniPlotView
{
    protected TelemetryPlot? Plot;
    
    public static readonly StyledProperty<TelemetryData?> TelemetryProperty =
        AvaloniaProperty.Register<SufniTelemetryPlotView, TelemetryData?>(nameof(Telemetry));
    
    public TelemetryData? Telemetry
    {
        get => GetValue(TelemetryProperty);
        set => SetValue(TelemetryProperty, value);
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
    }
}