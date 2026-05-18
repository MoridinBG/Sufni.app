using System;
using System.Collections.Generic;
using Sufni.App.Models;

namespace Sufni.App.Plots;

internal static class TelemetryDisplaySmoothing
{
    private const int LightTimeConstantMilliseconds = 50;
    private const int StrongTimeConstantMilliseconds = 150;

    public static double[] ApplyRegular(double[] samples, PlotSmoothingLevel level, double samplePeriodSeconds)
    {
        var timeConstantSeconds = GetTimeConstantMilliseconds(level) / 1000.0;
        if (timeConstantSeconds <= 0 ||
            samples.Length <= 2 ||
            !double.IsFinite(samplePeriodSeconds) ||
            samplePeriodSeconds <= 0)
        {
            return samples;
        }

        var forward = new double[samples.Length];
        var smoothed = new double[samples.Length];
        ApplyForwardRegular(samples, samplePeriodSeconds, timeConstantSeconds, forward);
        ApplyBackwardRegular(forward, samplePeriodSeconds, timeConstantSeconds, smoothed);
        return smoothed;
    }

    public static double[] ApplyIrregular(double[] xValues, double[] samples, PlotSmoothingLevel level)
    {
        var timeConstantSeconds = GetTimeConstantMilliseconds(level) / 1000.0;
        if (timeConstantSeconds <= 0 || samples.Length <= 2 || xValues.Length != samples.Length)
        {
            return samples;
        }

        var forward = new double[samples.Length];
        var smoothed = new double[samples.Length];
        ApplyForwardIrregular(xValues, samples, timeConstantSeconds, forward);
        ApplyBackwardIrregular(xValues, forward, timeConstantSeconds, smoothed);
        return smoothed;
    }

    public static int GetTimeConstantMilliseconds(PlotSmoothingLevel level)
    {
        return level switch
        {
            PlotSmoothingLevel.Light => LightTimeConstantMilliseconds,
            PlotSmoothingLevel.Strong => StrongTimeConstantMilliseconds,
            _ => 0,
        };
    }

    internal static double CalculateAlpha(double deltaSeconds, double timeConstantSeconds)
    {
        if (!double.IsFinite(deltaSeconds) ||
            !double.IsFinite(timeConstantSeconds) ||
            deltaSeconds <= 0 ||
            timeConstantSeconds <= 0)
        {
            return 0;
        }

        return 1.0 - Math.Exp(-deltaSeconds / timeConstantSeconds);
    }

    private static void ApplyForwardRegular(
        IReadOnlyList<double> samples,
        double samplePeriodSeconds,
        double timeConstantSeconds,
        double[] output)
    {
        ApplyForward(samples, index => samplePeriodSeconds, timeConstantSeconds, output);
    }

    private static void ApplyBackwardRegular(
        IReadOnlyList<double> samples,
        double samplePeriodSeconds,
        double timeConstantSeconds,
        double[] output)
    {
        ApplyBackward(samples, index => samplePeriodSeconds, timeConstantSeconds, output);
    }

    private static void ApplyForwardIrregular(
        double[] xValues,
        IReadOnlyList<double> samples,
        double timeConstantSeconds,
        double[] output)
    {
        ApplyForward(samples, index => index == 0 ? 0 : xValues[index] - xValues[index - 1], timeConstantSeconds, output, xValues);
    }

    private static void ApplyBackwardIrregular(
        double[] xValues,
        IReadOnlyList<double> samples,
        double timeConstantSeconds,
        double[] output)
    {
        ApplyBackward(samples, index => index == samples.Count - 1 ? 0 : xValues[index + 1] - xValues[index], timeConstantSeconds, output, xValues);
    }

    private static void ApplyForward(
        IReadOnlyList<double> samples,
        Func<int, double> getDeltaSeconds,
        double timeConstantSeconds,
        double[] output,
        IReadOnlyList<double>? xValues = null)
    {
        var smoothed = 0.0;
        var hasSmoothed = false;
        for (var index = 0; index < samples.Count; index++)
        {
            ApplySample(
                samples[index],
                xValues?[index],
                getDeltaSeconds(index),
                timeConstantSeconds,
                ref smoothed,
                ref hasSmoothed,
                out output[index]);
        }
    }

    private static void ApplyBackward(
        IReadOnlyList<double> samples,
        Func<int, double> getDeltaSeconds,
        double timeConstantSeconds,
        double[] output,
        IReadOnlyList<double>? xValues = null)
    {
        var smoothed = 0.0;
        var hasSmoothed = false;
        for (var index = samples.Count - 1; index >= 0; index--)
        {
            ApplySample(
                samples[index],
                xValues?[index],
                getDeltaSeconds(index),
                timeConstantSeconds,
                ref smoothed,
                ref hasSmoothed,
                out output[index]);
        }
    }

    private static void ApplySample(
        double value,
        double? xValue,
        double deltaSeconds,
        double timeConstantSeconds,
        ref double smoothed,
        ref bool hasSmoothed,
        out double output)
    {
        if (!double.IsFinite(value) ||
            xValue is { } timestamp && !double.IsFinite(timestamp))
        {
            hasSmoothed = false;
            output = value;
            return;
        }

        if (!hasSmoothed || deltaSeconds < 0)
        {
            smoothed = value;
            hasSmoothed = true;
            output = value;
            return;
        }

        var alpha = CalculateAlpha(deltaSeconds, timeConstantSeconds);
        smoothed += alpha * (value - smoothed);
        output = smoothed;
    }
}

internal sealed class TelemetryDisplayStreamingSmoother
{
    private double smoothed;
    private double lastTime;
    private bool hasSmoothed;
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
        var timeConstantSeconds = TelemetryDisplaySmoothing.GetTimeConstantMilliseconds(Level) / 1000.0;
        if (timeConstantSeconds <= 0 || values.Count == 0 || times.Count != values.Count)
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
            if (!double.IsFinite(time) || !double.IsFinite(value))
            {
                Reset();
                buffer[index] = value;
                continue;
            }

            if (!hasSmoothed || time < lastTime)
            {
                smoothed = value;
                lastTime = time;
                hasSmoothed = true;
                buffer[index] = value;
                continue;
            }

            var alpha = TelemetryDisplaySmoothing.CalculateAlpha(time - lastTime, timeConstantSeconds);
            smoothed += alpha * (value - smoothed);
            lastTime = time;
            buffer[index] = smoothed;
        }

        return new ArraySegment<double>(buffer, 0, values.Count);
    }

    public void Reset()
    {
        smoothed = 0;
        lastTime = 0;
        hasSmoothed = false;
    }
}
