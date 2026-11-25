namespace Sufni.Telemetry;

/// <summary>
/// Level arithmetic of the setup scale. Bins are equal steps of log level, twenty to one class step, so a
/// distribution of bins keeps its median to a twentieth of a class.
/// </summary>
public static class TerrainLevels
{
    /// <summary>The level ratio between adjacent descent classes.</summary>
    public const double Step = 1.5;

    public static readonly double BinWidth = Math.Log(Step) / 20;

    public static int Bin(double level) => (int)Math.Floor(Math.Log(level) / BinWidth);
}
