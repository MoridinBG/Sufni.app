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
    public TelemetryPlot? Plot;
    private bool applyingTimelineRange;

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

    private void OnTimelineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionTimelineLinkViewModel.VisibleRangeStart) or nameof(SessionTimelineLinkViewModel.VisibleRangeEnd))
        {
            ApplyTimelineRange();
        }
    }

    protected void UpdateTimelineRange()
    {
        if (applyingTimelineRange || AvaPlot is null || Telemetry is null || Timeline is null) return;

        var limits = AvaPlot.Plot.Axes.GetLimits();
        var duration = Telemetry.Metadata.Duration;
        if (duration <= 0) return;

        var startNormalized = Math.Clamp(limits.Left / duration, 0.0, 1.0);
        var endNormalized = Math.Clamp(limits.Right / duration, 0.0, 1.0);

        Timeline.SetVisibleRange(startNormalized, endNormalized);
    }

    private void ApplyTimelineRange()
    {
        if (applyingTimelineRange || AvaPlot is null || Telemetry is null || Timeline is null)
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
            AvaPlot.Plot.Axes.SetLimitsX(Timeline.VisibleRangeStart * duration, Timeline.VisibleRangeEnd * duration);
            AvaPlot.Refresh();
        }
        finally
        {
            applyingTimelineRange = false;
        }
    }
}