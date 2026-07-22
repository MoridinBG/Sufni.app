namespace Sufni.Telemetry;

public readonly record struct DigitizedSeries(
    double[] Bins,
    int[] Values);

public static class HistogramBuilder
{
    private const double BinEdgeTolerance = 0.0001;

    public static double[] Linspace(double min, double max, int count)
    {
        var step = (max - min) / (count - 1);
        var bins = new double[count];

        for (var index = 0; index < count; index++)
        {
            bins[index] = min + step * index;
        }

        return bins;
    }

    public static int[] Digitize(ReadOnlySpan<double> values, ReadOnlySpan<double> bins)
    {
        var indexes = new int[values.Length];
        if (bins.Length < 2)
        {
            return indexes;
        }

        var maxBinIndex = bins.Length - 2;
        for (var index = 0; index < values.Length; index++)
        {
            indexes[index] = DigitizeValue(values[index], bins, maxBinIndex);
        }

        return indexes;
    }

    /// <summary>
    /// Returns the histogram bin index for <paramref name="value"/>.
    /// Interior bins use [lower, upper) intervals, exact interior edges are
    /// assigned to the upper bin, and the final edge is included in the last bin.
    /// Values outside the bin range are clamped to the first or last bin.
    /// </summary>
    public static int DigitizeValue(double value, ReadOnlySpan<double> bins)
    {
        if (bins.Length < 2)
        {
            return 0;
        }

        return DigitizeValue(value, bins, bins.Length - 2);
    }

    public static DigitizedSeries DigitizeVelocity(double[] velocity, double step)
    {
        var bins = CreateVelocityBins(velocity, step);
        return new DigitizedSeries(bins, Digitize(velocity, bins));
    }

    internal static double[] CreateVelocityBins(ReadOnlySpan<double> velocity, double step)
    {
        if (velocity.IsEmpty)
        {
            throw new InvalidOperationException("Velocity samples are required.");
        }

        var minimum = velocity[0];
        var maximum = velocity[0];
        for (var index = 1; index < velocity.Length; index++)
        {
            minimum = Math.Min(minimum, velocity[index]);
            maximum = Math.Max(maximum, velocity[index]);
        }

        var min = (Math.Floor(minimum / step) - 0.5) * step;
        var max = (Math.Floor(maximum / step) + 1.5) * step;
        return Linspace(min, max, (int)((max - min) / step) + 1);
    }

    private static int DigitizeValue(double value, ReadOnlySpan<double> bins, int maxBinIndex)
    {
        var binIndex = bins.BinarySearch(value);
        if (binIndex < 0)
        {
            binIndex = ~binIndex;
        }

        if (value >= bins[^1] ||
            binIndex >= bins.Length ||
            Math.Abs(value - bins[binIndex]) > BinEdgeTolerance)
        {
            binIndex -= 1;
        }

        return Math.Clamp(binIndex, 0, maxBinIndex);
    }
}
