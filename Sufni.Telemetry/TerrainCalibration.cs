namespace Sufni.Telemetry;

public sealed record TerrainCalibration(double CalmBoundary, double RoughBoundary)
{
    public const int Version = 1;
    public const double ReferenceSpeed = 7;
    public const double FrameSpeedExponent = 0.7;
    public const double LowersSpeedExponent = 0.9;
    public const double ClippedShareLimit = 0.02;
    public const int FullScaleCount = 32760;
    private static readonly TerrainCalibration Frame = new(1.4, 2.2);
    private static readonly TerrainCalibration Lowers = new(2.5, 3.8);

    public static TerrainCalibration? For(ImuLocation location, double scale) => (location, scale) switch
    {
        (ImuLocation.Frame, 4096) => Frame,
        (ImuLocation.Fork, 4096) => Lowers,
        _ => null
    };

    internal static double? LowersClassValue(SensorRoughness? frame, SensorRoughness? lowers) =>
        frame is not null && lowers?.ClippedShare > ClippedShareLimit ? null : lowers?.Normalised;

    public static RoughnessClass Classify(double normalised) => normalised < 0 ? RoughnessClass.Calm :
        normalised < 1 ? RoughnessClass.Medium : RoughnessClass.Rough;

    public double Normalise(double speedRemoved) => Math.Log(speedRemoved / CalmBoundary) / Math.Log(RoughBoundary / CalmBoundary);
}
