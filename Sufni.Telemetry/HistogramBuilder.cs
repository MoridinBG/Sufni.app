namespace Sufni.Telemetry;

public readonly record struct DigitizedSeries(
    double[] Bins,
    int[] Values);

public static class HistogramBuilder
{
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

    public static int[] Digitize(double[] values, double[] bins)
    {
        var indexes = new int[values.Length];
        var maxBinIndex = bins.Length - 2;
        for (var index = 0; index < values.Length; index++)
        {
            var binIndex = Array.BinarySearch(bins, values[index]);
            if (binIndex < 0)
            {
                binIndex = ~binIndex;
            }

            if (values[index] >= bins[^1] || Math.Abs(values[index] - bins[binIndex]) > 0.0001)
            {
                binIndex -= 1;
            }

            indexes[index] = Math.Clamp(binIndex, 0, maxBinIndex);
        }

        return indexes;
    }

    public static int DigitizeValue(double value, double[] bins)
    {
        if (bins.Length < 2)
        {
            return 0;
        }

        return DigitizeValue(value, bins, bins.Length - 2);
    }

    public static DigitizedSeries DigitizeVelocity(double[] velocity, double step)
    {
        var min = (Math.Floor(velocity.Min() / step) - 0.5) * step;
        var max = (Math.Floor(velocity.Max() / step) + 1.5) * step;
        var bins = Linspace(min, max, (int)((max - min) / step) + 1);
        return new DigitizedSeries(bins, Digitize(velocity, bins));
    }

    private static int DigitizeValue(double value, double[] bins, int maxBinIndex)
    {
        if (value <= bins[0])
        {
            return 0;
        }

        if (value >= bins[^1])
        {
            return maxBinIndex;
        }

        var index = Array.BinarySearch(bins, value);
        if (index >= 0)
        {
            return Math.Clamp(index - 1, 0, maxBinIndex);
        }

        return Math.Clamp(~index - 1, 0, maxBinIndex);
    }
}
