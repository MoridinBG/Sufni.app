using System;
using System.ComponentModel;
using Avalonia;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.Presentation;

using Sufni.App.Sessions.Plots;
using Sufni.App.Shared.Plots;
namespace Sufni.App.Shared.Views.Plots;

public abstract class SufniTimelinePlotView : SufniPlotView
{
    private bool applyingTimelineRange;

    public static readonly StyledProperty<IRecordedSessionTimeline?> TimelineProperty =
        AvaloniaProperty.Register<SufniTimelinePlotView, IRecordedSessionTimeline?>(nameof(Timeline));

    protected abstract TelemetryPlot? TimelinePlot { get; }
    protected abstract double? TimelineDurationSeconds { get; }

    public IRecordedSessionTimeline? Timeline
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

            if (e.OldValue is IRecordedSessionTimeline oldTimeline)
            {
                oldTimeline.PropertyChanged -= OnTimelinePropertyChanged;
                oldTimeline.VisibleRangeChanged -= OnTimelineVisibleRangeChanged;
            }

            if (e.NewValue is IRecordedSessionTimeline newTimeline)
            {
                newTimeline.PropertyChanged += OnTimelinePropertyChanged;
                newTimeline.VisibleRangeChanged += OnTimelineVisibleRangeChanged;
                ApplyTimelineCursor();
                ApplyTimelineRange();
            }
        };
    }

    protected override void OnViewportChanged() => UpdateTimelineRange();

    protected override void RefreshPlotCore(PlotInvalidation invalidation)
    {
        if (TimelinePlot is not RecordedTimeSeriesPlot recordedPlot)
        {
            ClearCursorOverlay();
            base.RefreshPlotCore(invalidation);
            return;
        }

        var renderCursorExternally = !recordedPlot.IsCursorReadoutVisible;
        if (!recordedPlot.TryConfigureCursorRendering(renderCursorExternally, out var cursorPosition))
        {
            ClearCursorOverlay();
            base.RefreshPlotCore(invalidation);
            return;
        }

        if (!renderCursorExternally)
        {
            ClearCursorOverlay();
            base.RefreshPlotCore(invalidation);
            return;
        }

        var cursorReady = UpdateCursorOverlay(cursorPosition, CurrentTheme.Plot.Cursor.Line);
        if (invalidation == PlotInvalidation.Cursor && cursorReady)
        {
            return;
        }

        base.RefreshPlotCore(invalidation);
    }

    protected void ApplyTimelineCursor()
    {
        var plot = TimelinePlot;
        if (plot is null || Timeline is null || TimelineDurationSeconds is not { } duration || duration <= 0)
        {
            return;
        }

        var invalidation = PlotInvalidation.Cursor;
        if (plot.IsCursorReadoutVisible)
        {
            invalidation |= PlotInvalidation.Overlay;
        }

        if (Timeline.NormalizedCursorPosition is { } normalizedCursorPosition)
        {
            plot.SetCursorPosition(normalizedCursorPosition * duration);
        }
        else
        {
            plot.SetCursorPosition(double.NaN);
        }

        RefreshPlot(invalidation);
    }

    protected void UpdateTimelineRange()
    {
        if (applyingTimelineRange || !HasPlotControl || Timeline is null || TimelineDurationSeconds is not { } duration || duration <= 0)
        {
            return;
        }

        var limits = PlotControl.Plot.Axes.GetLimits();
        var (left, right) = AxisRangeConstraints.Constrain(
            limits.Left,
            limits.Right,
            0,
            duration,
            duration * PlotZoomFractions.TimeSeries);
        var startNormalized = Math.Clamp(left / duration, 0.0, 1.0);
        var endNormalized = Math.Clamp(right / duration, 0.0, 1.0);

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
            RefreshPlot(PlotInvalidation.Viewport);
        }
        finally
        {
            applyingTimelineRange = false;
        }
    }

    private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IRecordedSessionTimeline.NormalizedCursorPosition))
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
