namespace Sufni.Telemetry;

public sealed record TerrainCalibration(double CalmBoundary, double RoughBoundary)
{
    public const int Version = 2;
    /// <summary>Frame 1–80 Hz RMS in g, without speed correction, from which a climb is technical. Provisional.</summary>
    public const double TechnicalClimbLevel = 0.26;
    private static readonly TerrainCalibration Frame = new(1.4, 2.2);
    private static readonly TerrainCalibration Lowers = new(2.5, 3.8);

    public static TerrainCalibration? For(ImuLocation location, double scale) => (location, scale) switch
    {
        (ImuLocation.Frame, 4096) => Frame,
        (ImuLocation.Fork, 4096) => Lowers,
        _ => null
    };

    /// <summary>Levels one log step wide: the calm and rough boundaries sit at 0 and 1.</summary>
    public static RoughnessClass Classify(double normalised) => normalised < -1 ? RoughnessClass.Smooth :
        normalised < 0 ? RoughnessClass.Calm : normalised < 1 ? RoughnessClass.Medium :
        normalised < 2 ? RoughnessClass.Rough : RoughnessClass.VeryRough;

    public static RoughnessClass ClassifyClimb(double frameLevel) =>
        frameLevel < TechnicalClimbLevel ? RoughnessClass.SmoothClimb : RoughnessClass.TechnicalClimb;

    public double Normalise(double speedRemoved) => Math.Log(speedRemoved / CalmBoundary) / Math.Log(RoughBoundary / CalmBoundary);

    /// <summary>The speed-removed levels, in g, at the boundaries between adjacent descent levels.</summary>
    public IReadOnlyList<double> LevelEdges =>
        [.. new[] { -1d, 0, 1, 2 }.Select(n => CalmBoundary * Math.Pow(RoughBoundary / CalmBoundary, n))];
}
