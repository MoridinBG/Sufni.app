using System;
using System.ComponentModel;
using Avalonia;
using Sufni.App.Plots;
using Sufni.App.Views.Plots;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;

namespace Sufni.App.DesktopViews.Plots;

public abstract class SufniTelemetryPlotView : SufniPlotView
{
    private TelemetryPlot? plot;
    private bool applyingTimelineRange;
    private bool hasPendingTelemetryLoad;

    protected TelemetryPlot PlotModel => plot!;
    public bool IsPlotReady => plot is not null && HasPlotControl;

    public static readonly StyledProperty<TelemetryData?> TelemetryProperty =
        AvaloniaProperty.Register<SufniTelemetryPlotView, TelemetryData?>(nameof(Telemetry));

    public TelemetryData? Telemetry
    {
        get => GetValue(TelemetryProperty);
        set => SetValue(TelemetryProperty, value);
    }

    public static readonly StyledProperty<SessionTimelineLinkViewModel?> TimelineProperty =
        AvaloniaProperty.Register<SufniTelemetryPlotView, SessionTimelineLinkViewModel?>(nameof(Timeline));

    public SessionTimelineLinkViewModel? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    protected SufniTelemetryPlotView()
    {
        // Populate the ScottPlot plot when the Telemetry property is set.
        PropertyChanged += (_, e) =>
        {
            if (e.NewValue is null || plot is null || !HasPlotControl) return;

            switch (e.Property.Name)
            {
                case nameof(Telemetry):
                    if (!IsEffectivelyVisible)
                    {
                        hasPendingTelemetryLoad = true;
                        return;
                    }

                    LoadTelemetryIntoPlot((TelemetryData)e.NewValue);
                    break;
            }

            RefreshPlot();
        };

        // When the plot becomes visible again (tab switch), apply any deferred telemetry load.
        // EffectiveViewportChanged fires when the control's effective visibility changes,
        // including when a parent's IsVisible is toggled.
        EffectiveViewportChanged += (_, _) =>
        {
            if (!IsEffectivelyVisible || !hasPendingTelemetryLoad)
            {
                return;
            }

            if (Telemetry is { } data && plot is not null && HasPlotControl)
            {
                hasPendingTelemetryLoad = false;
                LoadTelemetryIntoPlot(data);
            }
        };

        // Subscribe to shared timeline range changes for media → plot linking.
        PropertyChanged += (_, e) =>
        {
            if (e.Property.Name != nameof(Timeline)) return;
            if (e.OldValue is SessionTimelineLinkViewModel oldTimeline)
            {
                oldTimeline.PropertyChanged -= OnTimelineChanged;
            }

            if (e.NewValue is SessionTimelineLinkViewModel newTimeline)
            {
                newTimeline.PropertyChanged += OnTimelineChanged;
                ApplyTimelineRange();
            }
        };
    }

    protected void SetPlotModel(TelemetryPlot plotModel)
    {
        plot = plotModel;
    }

    public void SetCursorPosition(double position)
    {
        plot?.SetCursorPosition(position);
        RefreshPlot();
    }

    public void LinkXAxisWith(SufniTelemetryPlotView other)
    {
        if (!IsPlotReady || !other.IsPlotReady)
        {
            return;
        }

        PlotControl.Plot.Axes.Link(other.PlotControl, x: true, y: false);
        other.PlotControl.Plot.Axes.Link(PlotControl, x: true, y: false);
    }

    private void OnTimelineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionTimelineLinkViewModel.VisibleRangeStart) or nameof(SessionTimelineLinkViewModel.VisibleRangeEnd))
        {
            ApplyTimelineRange();
        }
    }

    private void LoadTelemetryIntoPlot(TelemetryData telemetryData)
    {
        if (plot is null || !HasPlotControl)
        {
            return;
        }

        plot.Clear();
        plot.LoadTelemetryData(telemetryData);
        RefreshPlot();
    }

    protected void UpdateTimelineRange()
    {
        if (applyingTimelineRange || !HasPlotControl || Telemetry is null || Timeline is null) return;

        var limits = PlotControl.Plot.Axes.GetLimits();
        var duration = Telemetry.Metadata.Duration;
        if (duration <= 0) return;

        var startNormalized = Math.Clamp(limits.Left / duration, 0.0, 1.0);
        var endNormalized = Math.Clamp(limits.Right / duration, 0.0, 1.0);

        Timeline.SetVisibleRange(startNormalized, endNormalized);
    }

    private void ApplyTimelineRange()
    {
        if (applyingTimelineRange || !HasPlotControl || Telemetry is null || Timeline is null)
        {
            return;
        }

        var duration = Telemetry.Metadata.Duration;
        if (duration <= 0)
        {
            return;
        }

        applyingTimelineRange = true;
        try
        {
            PlotControl.Plot.Axes.SetLimitsX(Timeline.VisibleRangeStart * duration, Timeline.VisibleRangeEnd * duration);
            RefreshPlot();
        }
        finally
        {
            applyingTimelineRange = false;
        }
    }
}