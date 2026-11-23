namespace Sufni.Telemetry;

public sealed record TerrainCalibration(double CalmBoundary, double RoughBoundary)
{
    public const int Version = 1;
    private static readonly TerrainCalibration Frame = new(1.4, 2.2);
    private static readonly TerrainCalibration Lowers = new(2.5, 3.8);

    public static TerrainCalibration? For(ImuLocation location, double scale) => (location, scale) switch
    {
        (ImuLocation.Frame, 4096) => Frame,
        (ImuLocation.Fork, 4096) => Lowers,
        _ => null
    };

    public static RoughnessClass Classify(double normalised) => normalised < 0 ? RoughnessClass.Calm :
        normalised < 1 ? RoughnessClass.Medium : RoughnessClass.Rough;

    public double Normalise(double speedRemoved) => Math.Log(speedRemoved / CalmBoundary) / Math.Log(RoughBoundary / CalmBoundary);
}
