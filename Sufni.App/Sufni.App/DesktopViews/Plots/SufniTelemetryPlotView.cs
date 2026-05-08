using Avalonia;
using Sufni.App.Models;
using Sufni.App.Plots;
using Sufni.Telemetry;

namespace Sufni.App.DesktopViews.Plots;

public abstract class SufniTelemetryPlotView : SufniTimeSeriesPlotView
{
    protected override double? TimelineDurationSeconds => Telemetry?.Metadata.Duration;

    public static readonly StyledProperty<TelemetryData?> TelemetryProperty =
        AvaloniaProperty.Register<SufniTelemetryPlotView, TelemetryData?>(nameof(Telemetry));

    public TelemetryData? Telemetry
    {
        get => GetValue(TelemetryProperty);
        set => SetValue(TelemetryProperty, value);
    }

    public static readonly StyledProperty<int?> MaximumDisplayHzProperty =
        AvaloniaProperty.Register<SufniTelemetryPlotView, int?>(nameof(MaximumDisplayHz));

    public int? MaximumDisplayHz
    {
        get => GetValue(MaximumDisplayHzProperty);
        set => SetValue(MaximumDisplayHzProperty, value);
    }

    protected override TelemetryData? MarkerSource => Telemetry;

    protected SufniTelemetryPlotView()
    {
        PropertyChanged += (_, e) =>
        {
            switch (e.Property.Name)
            {
                case nameof(Telemetry):
                case nameof(MaximumDisplayHz):
                    RequestReload();
                    break;
            }

            RefreshPlot();
        };
    }

    protected void ReloadTelemetry()
    {
        RequestReload();
    }

    protected override void ApplyPlotOptions(TelemetryPlot plotModel)
    {
        base.ApplyPlotOptions(plotModel);
        plotModel.MaximumDisplayHz = MaximumDisplayHz;
    }

    protected override void OnAnalysisRangeChanged()
    {
        if (HasPlotModel && PlotModel is RecordedTimeSeriesPlot)
        {
            base.OnAnalysisRangeChanged();
            return;
        }

        if (Telemetry is not null)
        {
            RequestReload();
        }
    }

    protected override bool CanLoadPlotData => Telemetry is not null;

    protected override void LoadPlotData(TelemetryPlot plotModel)
    {
        plotModel.LoadTelemetryData(Telemetry!);
    }

}
