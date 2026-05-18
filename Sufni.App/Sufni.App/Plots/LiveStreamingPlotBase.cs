using System;
using System.Collections.Generic;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Models;
using Sufni.App.Theming;

namespace Sufni.App.Plots;

public abstract class LiveStreamingPlotBase : TelemetryPlot
{
    private readonly string title;
    private readonly int visibleWindowDurationMilliseconds;
    private double latestTimeSeconds;
    private bool hasTiming;
    private double configuredMinimumY;
    private double configuredMaximumY;

    protected LiveStreamingPlotBase(
        Plot plot,
        string title,
        int capacity,
        int visibleWindowDurationMilliseconds,
        double minimumY,
        double maximumY,
        bool hideRightAxis,
        SufniTheme? theme = null)
        : base(plot, theme)
    {
        this.title = title;
        this.visibleWindowDurationMilliseconds = visibleWindowDurationMilliseconds;
        Capacity = capacity;
        HideRightAxis = hideRightAxis;
        CursorLine = AddTimeSeriesCursorLine(isVisible: false);
        ApplyFrame();
        SetVerticalLimits(minimumY, maximumY);
    }

    protected int Capacity { get; }
    public int SampleCapacity => Capacity;
    public VerticalLine CursorLine { get; }

    public bool HasTiming => hasTiming;
    public double LatestTimeSeconds => latestTimeSeconds;

    public void SetHideRightAxis(bool hideRightAxis)
    {
        if (HideRightAxis == hideRightAxis)
        {
            return;
        }

        HideRightAxis = hideRightAxis;
        ApplyFrame();
    }

    private void ApplyFrame()
    {
        ConfigureTimeSeriesFrame(title, coordinate => $"{CoordinateToTime(coordinate):0.###}");
        ConfigureSymmetricValueTicks(20);
        Plot.Axes.SetLimitsX(0, Capacity);
        SetMirroredValueRange(configuredMinimumY, configuredMaximumY);
    }

    protected LivePlotChannel CreateChannel(Color color, string legendText)
    {
        var streamer = Plot.Add.DataStreamer(Capacity);
        streamer.ViewScrollLeft();
        streamer.ManageAxisLimits = false;
        streamer.Color = color;
        streamer.LineWidth = 2;
        streamer.LegendText = legendText;
        return new LivePlotChannel(streamer);
    }

    public void SetVerticalLimits(double minimumY, double maximumY)
    {
        if (!double.IsFinite(minimumY) || !double.IsFinite(maximumY))
        {
            configuredMinimumY = 0;
            configuredMaximumY = 1;
        }
        else if (maximumY <= minimumY)
        {
            var padding = Math.Max(0.1, Math.Abs(maximumY) * 0.1);
            configuredMinimumY = minimumY - padding;
            configuredMaximumY = maximumY + padding;
        }
        else
        {
            configuredMinimumY = minimumY;
            configuredMaximumY = maximumY;
        }

        SetMirroredValueRange(configuredMinimumY, configuredMaximumY);
    }

    protected void UpdateTiming(IReadOnlyList<double> times)
    {
        if (times.Count == 0)
        {
            return;
        }

        hasTiming = true;
        latestTimeSeconds = Math.Max(latestTimeSeconds, times[^1]);

    }

    public double? CoordinateToNormalizedTime(double coordinate)
    {
        if (!hasTiming || latestTimeSeconds <= 0)
        {
            return null;
        }

        return CoordinateToTime(coordinate) / latestTimeSeconds;
    }

    public void SetCursorFromNormalized(double? normalizedTime)
    {
        if (normalizedTime is null || !hasTiming || latestTimeSeconds <= 0)
        {
            CursorLine.Position = double.NaN;
            CursorLine.IsVisible = false;
            return;
        }

        CursorLine.Position = TimeToCoordinate(Math.Clamp(normalizedTime.Value, 0, 1) * latestTimeSeconds);
        CursorLine.IsVisible = true;
    }

    public (double Start, double End) GetNormalizedVisibleRange()
    {
        if (!hasTiming || latestTimeSeconds <= 0)
        {
            return (0, 1);
        }

        var limits = Plot.Axes.GetLimits();
        return (
            CoordinateToTime(limits.Left) / latestTimeSeconds,
            CoordinateToTime(limits.Right) / latestTimeSeconds);
    }

    public void ApplyVisibleRange(double startNormalized, double endNormalized)
    {
        if (!hasTiming || latestTimeSeconds <= 0)
        {
            return;
        }

        var windowStart = Math.Max(0, latestTimeSeconds - VisibleWindowDurationSeconds);
        var startTime = Math.Clamp(startNormalized * latestTimeSeconds, windowStart, latestTimeSeconds);
        var endTime = Math.Clamp(endNormalized * latestTimeSeconds, windowStart, latestTimeSeconds);
        if (endTime <= startTime)
        {
            startTime = windowStart;
            endTime = latestTimeSeconds;
        }

        Plot.Axes.SetLimitsX(TimeToCoordinate(startTime), TimeToCoordinate(endTime));
    }

    public virtual void Reset()
    {
        foreach (var channel in Channels)
        {
            channel.Reset();
        }

        hasTiming = false;
        latestTimeSeconds = 0;
        CursorLine.Position = double.NaN;
        CursorLine.IsVisible = false;
        Plot.Axes.SetLimitsX(0, Capacity);
        SetMirroredValueRange(configuredMinimumY, configuredMaximumY);
    }

    protected abstract IEnumerable<LivePlotChannel> Channels { get; }

    private double CoordinateToTime(double coordinate)
    {
        if (!hasTiming)
        {
            return 0;
        }

        var fraction = Math.Clamp(coordinate / Capacity, 0, 1);
        return VisibleWindowStartSeconds + fraction * VisibleWindowDurationSeconds;
    }

    private double TimeToCoordinate(double timeSeconds)
    {
        if (!hasTiming)
        {
            return Capacity;
        }

        var clamped = Math.Clamp(timeSeconds, VisibleWindowStartSeconds, latestTimeSeconds);
        if (VisibleWindowDurationSeconds <= 0)
        {
            return Capacity;
        }

        return (clamped - VisibleWindowStartSeconds) / VisibleWindowDurationSeconds * Capacity;
    }

    private double VisibleWindowStartSeconds => Math.Max(0, latestTimeSeconds - VisibleWindowDurationSeconds);

    private double VisibleWindowDurationSeconds => hasTiming
        ? Math.Min(latestTimeSeconds, visibleWindowDurationMilliseconds / 1000.0)
        : 0;

    protected sealed class LivePlotChannel(DataStreamer streamer)
    {
        private readonly TelemetryDisplayStreamingSmoother smoother = new();
        private double[] smoothingScratch = [];

        public void Append(
            IReadOnlyList<double> times,
            IReadOnlyList<double> values,
            PlotSmoothingLevel smoothingLevel)
        {
            if (values.Count == 0)
            {
                return;
            }

            smoother.Level = smoothingLevel;
            streamer.AddRange(smoother.Apply(times, values, ref smoothingScratch));
        }

        public void Reset()
        {
            smoother.Reset();
            streamer.Clear(double.NaN);
        }
    }
}
