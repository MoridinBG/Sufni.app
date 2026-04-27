using System;
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

    public static readonly StyledProperty<int?> MaximumDisplayHzProperty =
        AvaloniaProperty.Register<SufniTelemetryPlotView, int?>(nameof(MaximumDisplayHz));

    public int? MaximumDisplayHz
    {
        get => GetValue(MaximumDisplayHzProperty);
        set => SetValue(MaximumDisplayHzProperty, value);
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
            switch (e.Property.Name)
            {
                case nameof(Telemetry):
                    if (e.NewValue is not TelemetryData telemetryData)
                    {
                        hasPendingTelemetryLoad = false;
                        return;
                    }

                    if (!CanLoadTelemetryNow())
                    {
                        hasPendingTelemetryLoad = true;
                        return;
                    }

                    hasPendingTelemetryLoad = false;
                    LoadTelemetryIntoPlot(telemetryData);
                    break;

                case nameof(IsVisible):
                    TryApplyPendingTelemetryLoad();
                    break;

                case nameof(MaximumDisplayHz):
                    if (Telemetry is not null)
                    {
                        hasPendingTelemetryLoad = true;
                        TryApplyPendingTelemetryLoad();
                    }
                    break;
            }

            RefreshPlot();
        };

        // When the plot becomes visible again (tab switch), apply any deferred telemetry load.
        // EffectiveViewportChanged fires when the control's effective visibility changes,
        // including when a parent's IsVisible is toggled.
        EffectiveViewportChanged += (_, _) =>
        {
            TryApplyPendingTelemetryLoad();
        };

        // Subscribe to shared timeline range changes for media → plot linking.
        PropertyChanged += (_, e) =>
        {
            if (e.Property.Name != nameof(Timeline)) return;
            if (e.OldValue is SessionTimelineLinkViewModel oldTimeline)
            {
                oldTimeline.VisibleRangeChanged -= OnTimelineVisibleRangeChanged;
            }

            if (e.NewValue is SessionTimelineLinkViewModel newTimeline)
            {
                newTimeline.VisibleRangeChanged += OnTimelineVisibleRangeChanged;
                ApplyTimelineRange();
            }
        };
    }

    protected void SetPlotModel(TelemetryPlot plotModel)
    {
        plot = plotModel;
        TryApplyPendingTelemetryLoad();
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

    private void OnTimelineVisibleRangeChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(Timeline?.VisibleRangeChangeSource, this))
        {
            return;
        }

        ApplyTimelineRange();
    }

    private void LoadTelemetryIntoPlot(TelemetryData telemetryData)
    {
        if (plot is null || !HasPlotControl)
        {
            return;
        }

        plot.MaximumDisplayHz = MaximumDisplayHz;
        plot.Clear();
        plot.LoadTelemetryData(telemetryData);
        RefreshPlot();
    }

    private bool CanLoadTelemetryNow()
    {
        return plot is not null && HasPlotControl && IsEffectivelyVisible;
    }

    private void TryApplyPendingTelemetryLoad()
    {
        if (!hasPendingTelemetryLoad || !CanLoadTelemetryNow() || Telemetry is not { } data)
        {
            return;
        }

        hasPendingTelemetryLoad = false;
        LoadTelemetryIntoPlot(data);
    }

    protected override void OnViewportChanged() => UpdateTimelineRange();

    protected void UpdateTimelineRange()
    {
        if (applyingTimelineRange || !HasPlotControl || Telemetry is null || Timeline is null) return;

        var limits = PlotControl.Plot.Axes.GetLimits();
        var duration = Telemetry.Metadata.Duration;
        if (duration <= 0) return;

        var startNormalized = Math.Clamp(limits.Left / duration, 0.0, 1.0);
        var endNormalized = Math.Clamp(limits.Right / duration, 0.0, 1.0);

        Timeline.SetVisibleRange(startNormalized, endNormalized, this);
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