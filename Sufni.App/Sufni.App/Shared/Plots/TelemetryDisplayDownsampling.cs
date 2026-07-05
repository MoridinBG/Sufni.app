using System;

namespace Sufni.App.Shared.Plots;

internal static class TelemetryDisplayDownsampling
{
    public static (double[] Samples, double Step) Prepare(double[] samples, int sampleRate, int? maximumDisplayHz)
    {
        var step = sampleRate > 0 ? 1.0 / sampleRate : 0.0;

        if (samples.Length <= 1 || sampleRate <= 0 || maximumDisplayHz is not > 0 || sampleRate <= maximumDisplayHz.Value)
        {
            return (samples, step);
        }

        var stride = GetStride(sampleRate, maximumDisplayHz.Value);
        var downsampled = new double[(samples.Length + stride - 1) / stride];
        var writeIndex = 0;

        for (var readIndex = 0; readIndex < samples.Length; readIndex += stride)
        {
            downsampled[writeIndex++] = samples[readIndex];
        }

        return (downsampled, step * stride);
    }

    public static (double[] XValues, double[] YValues) PrepareIrregular(
        double[] xValues,
        double[] yValues,
        int? maximumDisplayHz)
    {
        if (maximumDisplayHz is not > 0 ||
            xValues.Length != yValues.Length ||
            xValues.Length < 3)
        {
            return (xValues, yValues);
        }

        var xSpan = xValues[^1] - xValues[0];
        if (!double.IsFinite(xSpan) || xSpan <= 0)
        {
            return (xValues, yValues);
        }

        var strideValue = Math.Ceiling(xValues.Length / (xSpan * maximumDisplayHz.Value));
        if (!double.IsFinite(strideValue) || strideValue <= 1)
        {
            return (xValues, yValues);
        }

        var stride = (int)strideValue;
        var displayCount = (xValues.Length + stride - 1) / stride;
        if ((xValues.Length - 1) % stride != 0)
        {
            displayCount++;
        }

        var displayXValues = new double[displayCount];
        var displayYValues = new double[displayCount];
        var writeIndex = 0;
        var lastReadIndex = 0;

        for (var readIndex = 0; readIndex < xValues.Length; readIndex += stride)
        {
            displayXValues[writeIndex] = xValues[readIndex];
            displayYValues[writeIndex] = yValues[readIndex];
            writeIndex++;
            lastReadIndex = readIndex;
        }

        if (lastReadIndex != xValues.Length - 1)
        {
            displayXValues[writeIndex] = xValues[^1];
            displayYValues[writeIndex] = yValues[^1];
        }

        return (displayXValues, displayYValues);
    }

    public static int GetStride(int sampleRate, int maximumDisplayHz)
    {
        if (sampleRate <= 0 || maximumDisplayHz <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Ceiling(sampleRate / (double)maximumDisplayHz));
    }
}
