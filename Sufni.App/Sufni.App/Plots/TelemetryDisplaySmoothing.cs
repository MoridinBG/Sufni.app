using System;
using System.Collections.Generic;
using Sufni.App.Models;

namespace Sufni.App.Plots;

internal static class TelemetryDisplaySmoothing
{
    private const int LightWindowMilliseconds = 9;
    private const int StrongWindowMilliseconds = 31;

    public static double[] ApplyRegular(double[] samples, PlotSmoothingLevel level, double samplePeriodSeconds)
    {
        var radius = (GetWindowSize(level, samplePeriodSeconds) - 1) / 2;
        if (radius == 0 || samples.Length <= 2)
        {
            return samples;
        }

        var smoothed = new double[samples.Length];
        var start = 0;
        var end = -1;
        var sum = 0.0;

        for (var index = 0; index < samples.Length; index++)
        {
            var targetStart = Math.Max(0, index - radius);
            var targetEnd = Math.Min(samples.Length - 1, index + radius);

            while (end < targetEnd)
            {
                sum += samples[++end];
            }

            while (start < targetStart)
            {
                sum -= samples[start++];
            }

            smoothed[index] = sum / (end - start + 1);
        }

        return smoothed;
    }

    public static double[] ApplyIrregular(double[] xValues, double[] samples, PlotSmoothingLevel level)
    {
        var windowSeconds = GetWindowDurationMilliseconds(level) / 1000.0;
        if (windowSeconds <= 0 || samples.Length <= 2 || xValues.Length != samples.Length)
        {
            return samples;
        }

        var radiusSeconds = windowSeconds / 2.0;
        var smoothed = new double[samples.Length];
        var start = 0;
        var end = -1;
        var sum = 0.0;

        for (var index = 0; index < samples.Length; index++)
        {
            var center = xValues[index];
            var targetStart = center - radiusSeconds;
            var targetEnd = center + radiusSeconds;

            while (end + 1 < samples.Length && xValues[end + 1] <= targetEnd)
            {
                sum += samples[++end];
            }

            while (start <= end && xValues[start] < targetStart)
            {
                sum -= samples[start++];
            }

            smoothed[index] = start <= end ? sum / (end - start + 1) : samples[index];
        }

        return smoothed;
    }

    public static int GetWindowSize(PlotSmoothingLevel level, double samplePeriodSeconds)
    {
        var windowSeconds = GetWindowDurationMilliseconds(level) / 1000.0;
        if (windowSeconds <= 0 || !double.IsFinite(samplePeriodSeconds) || samplePeriodSeconds <= 0)
        {
            return 1;
        }

        var windowSize = Math.Max(1, (int)Math.Round(windowSeconds / samplePeriodSeconds));
        if (windowSize % 2 == 0)
        {
            windowSize++;
        }

        return windowSize;
    }

    public static int GetWindowDurationMilliseconds(PlotSmoothingLevel level)
    {
        return level switch
        {
            PlotSmoothingLevel.Light => LightWindowMilliseconds,
            PlotSmoothingLevel.Strong => StrongWindowMilliseconds,
            _ => 0,
        };
    }
}

internal sealed class TelemetryDisplayStreamingSmoother
{
    private readonly Queue<double> window = new();
    private readonly Queue<double> windowTimes = new();
    private double sum;
    private double? lastTime;
    private PlotSmoothingLevel level;

    public PlotSmoothingLevel Level
    {
        get => level;
        set
        {
            if (level == value)
            {
                return;
            }

            level = value;
            Reset();
        }
    }

    public IReadOnlyList<double> Apply(IReadOnlyList<double> times, IReadOnlyList<double> values, ref double[] buffer)
    {
        var windowSeconds = TelemetryDisplaySmoothing.GetWindowDurationMilliseconds(Level) / 1000.0;
        if (windowSeconds <= 0 || values.Count == 0 || times.Count != values.Count)
        {
            return values;
        }

        if (buffer.Length < values.Count)
        {
            buffer = new double[values.Count];
        }

        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            var time = times[index];
            if (!double.IsFinite(time))
            {
                Reset();
                buffer[index] = value;
                continue;
            }

            if (lastTime is { } previousTime && time < previousTime)
            {
                Reset();
            }

            window.Enqueue(value);
            windowTimes.Enqueue(time);
            sum += value;
            lastTime = time;

            while (windowTimes.Count > 0 && time - windowTimes.Peek() >= windowSeconds)
            {
                windowTimes.Dequeue();
                sum -= window.Dequeue();
            }

            buffer[index] = sum / window.Count;
        }

        return new ArraySegment<double>(buffer, 0, values.Count);
    }

    public void Reset()
    {
        window.Clear();
        windowTimes.Clear();
        sum = 0;
        lastTime = null;
    }
}
