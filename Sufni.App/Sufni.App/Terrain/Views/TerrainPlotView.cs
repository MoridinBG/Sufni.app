using Avalonia;
using Sufni.App.Shared.Plots;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Plots;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Views;

public abstract class TerrainPlotView : SufniTimeSeriesPlotView
{
    public static readonly StyledProperty<TerrainPresentation?> TerrainProperty =
        AvaloniaProperty.Register<TerrainPlotView, TerrainPresentation?>(nameof(Terrain));
    public static readonly StyledProperty<TelemetryData?> TelemetryProperty =
        AvaloniaProperty.Register<TerrainPlotView, TelemetryData?>(nameof(Telemetry));

    public TerrainPresentation? Terrain { get => GetValue(TerrainProperty); set => SetValue(TerrainProperty, value); }
    public TelemetryData? Telemetry { get => GetValue(TelemetryProperty); set => SetValue(TelemetryProperty, value); }

    protected override double? TimelineDurationSeconds => Telemetry?.Metadata.Duration;
    protected override TelemetryData? MarkerSource => Telemetry;
    protected override bool CanLoadPlotData => Terrain is not null && Telemetry is not null;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TerrainProperty || change.Property == TelemetryProperty) RequestReload();
    }
}

public sealed class TerrainClassStripPlotView : TerrainPlotView
{
    protected override void CreatePlot()
    {
        SetPlotModel(new TerrainClassStripPlot(PlotControl.Plot, CurrentTheme));
        InitializeCursorReadoutInteractions();
    }

    protected override void LoadPlotData(TelemetryPlot plotModel) =>
        ((TerrainClassStripPlot)plotModel).Load(Terrain!, Telemetry!);
}

public sealed class TerrainSignalPlotView : TerrainPlotView
{
    public static readonly StyledProperty<TerrainSignalKind> SignalKindProperty =
        AvaloniaProperty.Register<TerrainSignalPlotView, TerrainSignalKind>(nameof(SignalKind));

    public TerrainSignalKind SignalKind { get => GetValue(SignalKindProperty); set => SetValue(SignalKindProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SignalKindProperty) RequestReload();
    }

    protected override void CreatePlot()
    {
        SetPlotModel(new TerrainSignalPlot(PlotControl.Plot, CurrentTheme));
        InitializeCursorReadoutInteractions();
    }

    protected override void LoadPlotData(TelemetryPlot plotModel) =>
        ((TerrainSignalPlot)plotModel).Load(Terrain!, Telemetry!, SignalKind);
}
