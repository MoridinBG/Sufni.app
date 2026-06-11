using System.Collections.Generic;
using Avalonia;
using Sufni.App.Models;
using Sufni.App.Plots;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;

namespace Sufni.App.Views.Plots;

public class TrackSignalPlotView : SufniTimeSeriesPlotView
{
    protected override double? TimelineDurationSeconds => TimelineContext?.DurationSeconds;

    public static readonly StyledProperty<TrackSignalKind> SignalKindProperty =
        AvaloniaProperty.Register<TrackSignalPlotView, TrackSignalKind>(nameof(SignalKind));

    public TrackSignalKind SignalKind
    {
        get => GetValue(SignalKindProperty);
        set => SetValue(SignalKindProperty, value);
    }

    public static readonly StyledProperty<IReadOnlyList<TrackPoint>?> TrackPointsProperty =
        AvaloniaProperty.Register<TrackSignalPlotView, IReadOnlyList<TrackPoint>?>(nameof(TrackPoints));

    public IReadOnlyList<TrackPoint>? TrackPoints
    {
        get => GetValue(TrackPointsProperty);
        set => SetValue(TrackPointsProperty, value);
    }

    public static readonly StyledProperty<TrackTimeRange?> TimelineContextProperty =
        AvaloniaProperty.Register<TrackSignalPlotView, TrackTimeRange?>(nameof(TimelineContext));

    public TrackTimeRange? TimelineContext
    {
        get => GetValue(TimelineContextProperty);
        set => SetValue(TimelineContextProperty, value);
    }

    public static readonly StyledProperty<TelemetryData?> TelemetryProperty =
        AvaloniaProperty.Register<TrackSignalPlotView, TelemetryData?>(nameof(Telemetry));

    public TelemetryData? Telemetry
    {
        get => GetValue(TelemetryProperty);
        set => SetValue(TelemetryProperty, value);
    }

    protected override TelemetryData? MarkerSource => Telemetry;

    public TrackSignalPlotView()
    {
        PropertyChanged += (_, e) =>
        {
            switch (e.Property.Name)
            {
                case nameof(SignalKind):
                case nameof(TrackPoints):
                case nameof(TimelineContext):
                case nameof(Telemetry):
                    RequestReload();
                    break;
            }
        };
    }

    protected override void CreatePlot()
    {
        SetPlotModel(new TrackSignalPlot(PlotControl.Plot, CurrentTheme));
        InitializeCursorReadoutInteractions();
    }

    protected override bool CanLoadPlotData => TrackPoints is not null && TimelineContext is not null;

    protected override void LoadPlotData(TelemetryPlot plotModel)
    {
        if (plotModel is not TrackSignalPlot trackPlot || TrackPoints is null || TimelineContext is null)
        {
            return;
        }

        trackPlot.LoadTrackData(TrackPoints, TimelineContext.Value, Telemetry, SignalKind);
    }
}
