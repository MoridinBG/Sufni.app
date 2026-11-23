using System.Collections.Immutable;
using MessagePack;

namespace Sufni.Telemetry;

public enum RoughnessClass { Calm, Medium, Rough }
public enum SteepnessClass { Gentle, Moderate, Steep }
public enum RoughnessSource { Both, FrameOnly, LowersOnly }
public enum TerrainRoughnessState { Classified, ValuesOnly, NoSensor }
public enum TerrainSteepnessState { Available, NoFrameSensor, NoElevation, TooShort }
public enum TerrainUnavailableReason { NoTrack, NoMovement }

[MessagePackObject(keyAsPropertyName: true)]
public sealed record SensorRoughness(double Level, double LowBand, double MiddleBand, double HighBand,
    double SpeedRemoved, double? Normalised, double ClippedShare);

[MessagePackObject(keyAsPropertyName: true)]
public readonly record struct SuspensionWindowFigures(double RmsVelocity, double PeakCompression, double PeakRebound);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record TerrainWindow(double StartMetres, double StartSeconds, double EndSeconds, double MeanSpeed, bool Moving)
{
    public SensorRoughness? Frame { get; init; }
    public SensorRoughness? Lowers { get; init; }
    public RoughnessClass? Roughness { get; init; }
    public RoughnessSource? Source { get; init; }
    public double? Grade { get; init; }
    public SteepnessClass? Steepness { get; init; }

    [IgnoreMember]
    public double? NormalisedRoughness => Source switch
    {
        RoughnessSource.Both => (Frame?.Normalised + Lowers?.Normalised) / 2,
        RoughnessSource.FrameOnly => Frame?.Normalised,
        RoughnessSource.LowersOnly => Lowers?.Normalised,
        _ => null
    };
    public SuspensionWindowFigures? Front { get; init; }
    public SuspensionWindowFigures? Rear { get; init; }
}

[MessagePackObject(keyAsPropertyName: true)]
public readonly record struct TerrainMatchSample(double Metres, double Seconds, double LogLevel);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record TerrainProfile(ImmutableArray<TerrainWindow> Windows,
    ImmutableArray<TerrainMatchSample> FrameMatching, ImmutableArray<TerrainMatchSample> LowersMatching,
    TerrainRoughnessState RoughnessState, TerrainSteepnessState SteepnessState, double? FrameScale, double? LowersScale);

public closed record TerrainProfileResult
{
    private TerrainProfileResult() { }
    public sealed record Computed(TerrainProfile Profile) : TerrainProfileResult;
    public sealed record Unavailable(TerrainUnavailableReason Reason) : TerrainProfileResult;
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed record TerrainSummary(double ClassifiedMetres, double CalmShare, double MediumShare, double RoughShare,
    double? GentleShare, double? ModerateShare, double? SteepShare, RoughnessSource? DominantSource);
