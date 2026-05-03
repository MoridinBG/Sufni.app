using System;
using System.ComponentModel;
using Avalonia;
using Sufni.App.Plots;
using Sufni.App.Views.Plots;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.DesktopViews.Plots;

public abstract class SufniTimelinePlotView : SufniPlotView
{
    private bool applyingTimelineRange;

    public static readonly StyledProperty<SessionTimelineLinkViewModel?> TimelineProperty =
        AvaloniaProperty.Register<SufniTimelinePlotView, SessionTimelineLinkViewModel?>(nameof(Timeline));

    protected abstract TelemetryPlot? TimelinePlot { get; }
    protected abstract double? TimelineDurationSeconds { get; }

    public SessionTimelineLinkViewModel? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    protected SufniTimelinePlotView()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.Property.Name != nameof(Timeline))
            {
                return;
            }

            if (e.OldValue is SessionTimelineLinkViewModel oldTimeline)
            {
                oldTimeline.PropertyChanged -= OnTimelinePropertyChanged;
                oldTimeline.VisibleRangeChanged -= OnTimelineVisibleRangeChanged;
            }

            if (e.NewValue is SessionTimelineLinkViewModel newTimeline)
            {
                newTimeline.PropertyChanged += OnTimelinePropertyChanged;
                newTimeline.VisibleRangeChanged += OnTimelineVisibleRangeChanged;
                ApplyTimelineCursor();
                ApplyTimelineRange();
            }
        };
    }

    protected override void OnViewportChanged() => UpdateTimelineRange();

    protected void ApplyTimelineCursor()
    {
        var plot = TimelinePlot;
        if (plot is null || Timeline is null || TimelineDurationSeconds is not { } duration || duration <= 0)
        {
            return;
        }

        if (Timeline.NormalizedCursorPosition is { } normalizedCursorPosition)
        {
            plot.SetCursorPosition(normalizedCursorPosition * duration);
        }
        else
        {
            plot.SetCursorPosition(double.NaN);
        }

        RefreshPlot();
    }

    protected void UpdateTimelineRange()
    {
        if (applyingTimelineRange || !HasPlotControl || Timeline is null || TimelineDurationSeconds is not { } duration || duration <= 0)
        {
            return;
        }

        var limits = PlotControl.Plot.Axes.GetLimits();
        var startNormalized = Math.Clamp(limits.Left / duration, 0.0, 1.0);
        var endNormalized = Math.Clamp(limits.Right / duration, 0.0, 1.0);

        Timeline.SetVisibleRange(startNormalized, endNormalized, this);
    }

    protected void ApplyTimelineRange()
    {
        if (applyingTimelineRange || !HasPlotControl || Timeline is null || TimelineDurationSeconds is not { } duration || duration <= 0)
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

    private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionTimelineLinkViewModel.NormalizedCursorPosition))
        {
            ApplyTimelineCursor();
        }
    }

    private void OnTimelineVisibleRangeChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(Timeline?.VisibleRangeChangeSource, this))
        {
            return;
        }

        ApplyTimelineRange();
    }
}
