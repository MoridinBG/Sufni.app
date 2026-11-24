using System.Collections.Immutable;
using MessagePack;

namespace Sufni.Telemetry;

/// <summary>Five levels on the speed-corrected descent scale, then two climb levels from frame vibration.</summary>
public enum RoughnessClass { Smooth, Calm, Medium, Rough, VeryRough, SmoothClimb, TechnicalClimb }
/// <summary>Signed ground slope over 50 m. The two climb bands come first.</summary>
public enum GradeBand { SteepClimb, Climb, Flat, GentleDescent, Descent, SteepDescent, VerySteepDescent }
public enum RoughnessSource { Both, FrameOnly, LowersOnly }
public enum TerrainRoughnessState { Classified, ValuesOnly, NoSensor }
public enum TerrainGradeState { Available, NoFrameSensor, NoElevation, TooShort }
public enum TerrainUnavailableReason { NoTrack, NoMovement }
public enum TerrainSensor { Front, Rear, Frame, Lowers }

[MessagePackObject(keyAsPropertyName: true)]
public sealed record SensorRoughness(double Level, double LowBand, double MiddleBand, double HighBand,
    double SpeedRemoved, double? Normalised, double ClippedShare);

/// <summary>
/// One end's motion in a window. Velocities are in mm/s. Mean travel and the median travel gain of compression strokes
/// that start in the window are in mm; the stroke size is null without such a stroke.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public readonly record struct SuspensionWindowFigures(double RmsVelocity, double PeakCompression, double PeakRebound,
    double MeanTravel, double? MedianStrokeSize);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record TerrainWindow(double StartMetres, double StartSeconds, double EndSeconds, double MeanSpeed, bool Moving)
{
    public SensorRoughness? Frame { get; init; }
    public SensorRoughness? Lowers { get; init; }
    public RoughnessClass? Roughness { get; init; }
    public RoughnessSource? Source { get; init; }
    public double? Grade { get; init; }
    public GradeBand? Band { get; init; }

    [IgnoreMember]
    public bool Climbing => Band?.IsClimb() == true;

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
    TerrainRoughnessState RoughnessState, TerrainGradeState GradeState, double? FrameScale, double? LowersScale)
{
    /// <summary>Sensors that read zero in most measured moving windows; the profile holds no readings from them.</summary>
    public ImmutableArray<TerrainSensor> SilentSensors { get; init; } = [];
    public double? FrontMaxTravel { get; init; }
    public double? RearMaxTravel { get; init; }
}

public closed record TerrainProfileResult
{
    private TerrainProfileResult() { }
    public sealed record Computed(TerrainProfile Profile) : TerrainProfileResult;
    public sealed record Unavailable(TerrainUnavailableReason Reason) : TerrainProfileResult;
}

/// <summary>
/// Ride totals for the session list. <see cref="ClimbMetres"/> is null without grade. <see cref="RoughShare"/> is the
/// rough and very rough share of <see cref="ClassifiedMetres"/>, the non-climb distance with a roughness class.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record TerrainSummary(double MovingMetres, double? ClimbMetres, double ClassifiedMetres, double RoughShare,
    RoughnessSource? DominantSource);
