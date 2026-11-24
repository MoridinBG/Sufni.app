namespace Sufni.Telemetry;

public sealed record TerrainCalibration(double CalmBoundary, double RoughBoundary)
{
    public const int Version = 2;
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

    public double Normalise(double speedRemoved) => Math.Log(speedRemoved / CalmBoundary) / Math.Log(RoughBoundary / CalmBoundary);
}
