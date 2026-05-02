using System;
using System.Collections.Generic;
using Sufni.App.Models;

namespace Sufni.App.Plots;

internal static class TelemetryDisplaySmoothing
{
    public static double[] Apply(double[] samples, PlotSmoothingLevel level)
    {
        var radius = (GetWindowSize(level) - 1) / 2;
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

    public static int GetWindowSize(PlotSmoothingLevel level)
    {
        return level switch
        {
            PlotSmoothingLevel.Light => 9,
            PlotSmoothingLevel.Strong => 31,
            _ => 1,
        };
    }
}

internal sealed class TelemetryDisplayStreamingSmoother
{
    private readonly Queue<double> window = new();
    private double sum;
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

    public IReadOnlyList<double> Apply(IReadOnlyList<double> values, ref double[] buffer)
    {
        var windowSize = TelemetryDisplaySmoothing.GetWindowSize(Level);
        if (windowSize <= 1 || values.Count == 0)
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
            window.Enqueue(value);
            sum += value;

            while (window.Count > windowSize)
            {
                sum -= window.Dequeue();
            }

            buffer[index] = sum / window.Count;
        }

        return new ArraySegment<double>(buffer, 0, values.Count);
    }

    public void Reset()
    {
        window.Clear();
        sum = 0;
    }
}