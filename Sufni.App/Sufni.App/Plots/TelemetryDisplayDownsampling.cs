using System;

namespace Sufni.App.Plots;

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

    public static int GetStride(int sampleRate, int maximumDisplayHz)
    {
        if (sampleRate <= 0 || maximumDisplayHz <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Ceiling(sampleRate / (double)maximumDisplayHz));
    }
}