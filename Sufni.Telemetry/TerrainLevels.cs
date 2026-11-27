namespace Sufni.Telemetry;

/// <summary>
/// Level arithmetic of the setup scale. Bins are equal steps of log level, twenty to one class step, so a
/// distribution of bins keeps its median to a twentieth of a class.
/// </summary>
public static class TerrainLevels
{
    /// <summary>The level ratio between adjacent descent classes.</summary>
    public const double Step = 1.5;

    public const int BinsPerStep = 20;

    public static readonly double BinWidth = Math.Log(Step) / BinsPerStep;

    public static int Bin(double level) => (int)Math.Floor(Math.Log(level) / BinWidth);

    /// <summary>The level at the centre of a bin.</summary>
    public static double Median(int bin) => Math.Exp((bin + 0.5) * BinWidth);

    /// <summary>The level in class steps from the median, offset so that the median is the centre of Medium.</summary>
    public static double Value(double level, int medianBin) => Math.Log(level / Median(medianBin)) / Math.Log(Step) + 0.5;

    public static RoughnessClass Classify(double value) => value < -1 ? RoughnessClass.Smooth :
        value < 0 ? RoughnessClass.Calm : value < 1 ? RoughnessClass.Medium :
        value < 2 ? RoughnessClass.Rough : RoughnessClass.VeryRough;

    /// <summary>The class steps by which a value lies outside the values that <see cref="Classify"/> gives a descent class.</summary>
    public static double StepsOutside(double value, RoughnessClass roughness)
    {
        var lower = roughness == RoughnessClass.Smooth ? double.NegativeInfinity : (int)roughness - 2;
        var upper = roughness == RoughnessClass.VeryRough ? double.PositiveInfinity : (int)roughness - 1;
        return Math.Max(Math.Max(lower - value, value - upper), 0);
    }

    /// <summary>The levels at the boundaries between adjacent descent classes.</summary>
    public static IReadOnlyList<double> Edges(int medianBin) =>
        [.. new[] { -1.5, -0.5, 0.5, 1.5 }.Select(n => Median(medianBin) * Math.Pow(Step, n))];
}
