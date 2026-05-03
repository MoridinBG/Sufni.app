using System;
using System.Collections.Generic;
using Avalonia;
using Sufni.App.Models;
using Sufni.App.Plots;
using Sufni.Telemetry;

namespace Sufni.App.DesktopViews.Plots;

public class TrackSignalPlotDesktopView : SufniTimelinePlotView
{
    private TrackSignalPlot? plot;
    private bool hasPendingLoad;

    protected override TelemetryPlot? TimelinePlot => plot;
    protected override double? TimelineDurationSeconds => TimelineContext?.DurationSeconds;

    public static readonly StyledProperty<TrackSignalKind> SignalKindProperty =
        AvaloniaProperty.Register<TrackSignalPlotDesktopView, TrackSignalKind>(nameof(SignalKind));

    public TrackSignalKind SignalKind
    {
        get => GetValue(SignalKindProperty);
        set => SetValue(SignalKindProperty, value);
    }

    public static readonly StyledProperty<IReadOnlyList<TrackPoint>?> TrackPointsProperty =
        AvaloniaProperty.Register<TrackSignalPlotDesktopView, IReadOnlyList<TrackPoint>?>(nameof(TrackPoints));

    public IReadOnlyList<TrackPoint>? TrackPoints
    {
        get => GetValue(TrackPointsProperty);
        set => SetValue(TrackPointsProperty, value);
    }

    public static readonly StyledProperty<TrackTimeRange?> TimelineContextProperty =
        AvaloniaProperty.Register<TrackSignalPlotDesktopView, TrackTimeRange?>(nameof(TimelineContext));

    public TrackTimeRange? TimelineContext
    {
        get => GetValue(TimelineContextProperty);
        set => SetValue(TimelineContextProperty, value);
    }

    public static readonly StyledProperty<TelemetryData?> TelemetryProperty =
        AvaloniaProperty.Register<TrackSignalPlotDesktopView, TelemetryData?>(nameof(Telemetry));

    public TelemetryData? Telemetry
    {
        get => GetValue(TelemetryProperty);
        set => SetValue(TelemetryProperty, value);
    }

    public static readonly StyledProperty<PlotSmoothingLevel> SmoothingLevelProperty =
        AvaloniaProperty.Register<TrackSignalPlotDesktopView, PlotSmoothingLevel>(nameof(SmoothingLevel));

    public PlotSmoothingLevel SmoothingLevel
    {
        get => GetValue(SmoothingLevelProperty);
        set => SetValue(SmoothingLevelProperty, value);
    }

    public TrackSignalPlotDesktopView()
    {
        PropertyChanged += (_, e) =>
        {
            switch (e.Property.Name)
            {
                case nameof(SignalKind):
                case nameof(TrackPoints):
                case nameof(TimelineContext):
                case nameof(Telemetry):
                case nameof(SmoothingLevel):
                    RequestReload();
                    break;

                case nameof(IsVisible):
                    TryApplyPendingLoad();
                    break;
            }
        };

        EffectiveViewportChanged += (_, _) => TryApplyPendingLoad();
    }

    protected override void CreatePlot()
    {
        plot = new TrackSignalPlot(PlotControl.Plot);
        InitializeInteractions();
        TryApplyPendingLoad();
    }

    private void RequestReload()
    {
        hasPendingLoad = true;
        TryApplyPendingLoad();
    }

    private void TryApplyPendingLoad()
    {
        if (!hasPendingLoad || !CanLoadNow())
        {
            return;
        }

        hasPendingLoad = false;
        LoadTrackData();
    }

    private bool CanLoadNow()
    {
        return plot is not null && HasPlotControl && IsEffectivelyVisible;
    }

    private void LoadTrackData()
    {
        if (plot is null || TrackPoints is null || TimelineContext is null)
        {
            plot?.Clear();
            RefreshPlot();
            return;
        }

        plot.SmoothingLevel = SmoothingLevel;
        plot.Clear();
        plot.LoadTrackData(TrackPoints, TimelineContext.Value, Telemetry, SignalKind);
        ApplyTimelineCursor();
        ApplyTimelineRange();
        RefreshPlot();
    }

    private void InitializeInteractions()
    {
        void UpdateCursor(Avalonia.Input.PointerEventArgs args)
        {
            if (plot is null || TimelineContext is null || TimelineContext.Value.DurationSeconds <= 0)
            {
                return;
            }

            var point = args.GetPosition(PlotControl);
            var coords = PlotControl.Plot.GetCoordinates((float)point.X, (float)point.Y);
            var normalizedCursorPosition = Math.Clamp(coords.X / TimelineContext.Value.DurationSeconds, 0.0, 1.0);
            Timeline?.SetCursorPosition(normalizedCursorPosition);
            plot.SetCursorPosition(normalizedCursorPosition * TimelineContext.Value.DurationSeconds);
            RefreshPlot();
        }

        PlotControl.PointerPressed += (_, args) => UpdateCursor(args);
        PlotControl.PointerMoved += (_, args) => UpdateCursor(args);
        PlotControl.PointerReleased += (_, _) => UpdateTimelineRange();
        PlotControl.PointerWheelChanged += (_, _) => UpdateTimelineRange();
    }
}
