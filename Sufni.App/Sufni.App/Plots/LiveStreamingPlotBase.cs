using System;
using System.Collections.Generic;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Models;

namespace Sufni.App.Plots;

public abstract class LiveStreamingPlotBase : TelemetryPlot
{
    private readonly string title;
    private double latestTimeSeconds;
    private double samplePeriodSeconds = 1;
    private bool hasTiming;
    private double configuredMinimumY;
    private double configuredMaximumY;

    protected LiveStreamingPlotBase(
        Plot plot,
        string title,
        int capacity,
        double minimumY,
        double maximumY,
        bool hideRightAxis)
        : base(plot)
    {
        this.title = title;
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

    protected DataStreamer CreateStreamer(Color color)
    {
        var streamer = Plot.Add.DataStreamer(Capacity);
        streamer.ViewScrollLeft();
        streamer.ManageAxisLimits = false;
        streamer.Color = color;
        streamer.LineWidth = 2;
        return streamer;
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

        if (times.Count > 1)
        {
            var inferredPeriod = (times[^1] - times[0]) / (times.Count - 1);
            if (double.IsFinite(inferredPeriod) && inferredPeriod > 0)
            {
                samplePeriodSeconds = inferredPeriod;
            }
        }
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
        ClearStreamers();
        hasTiming = false;
        latestTimeSeconds = 0;
        samplePeriodSeconds = 1;
        CursorLine.Position = double.NaN;
        CursorLine.IsVisible = false;
        Plot.Axes.SetLimitsX(0, Capacity);
        SetMirroredValueRange(configuredMinimumY, configuredMaximumY);
    }

    protected abstract void ClearStreamers();

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
        ? Math.Min(latestTimeSeconds, samplePeriodSeconds * Capacity)
        : 0;
}
