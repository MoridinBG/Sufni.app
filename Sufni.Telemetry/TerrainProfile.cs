using System.Collections.Immutable;
using MessagePack;

namespace Sufni.Telemetry;

/// <summary>Five levels on the speed-corrected descent scale, then two climb levels from frame vibration.</summary>
public enum RoughnessClass { Smooth, Calm, Medium, Rough, VeryRough, SmoothClimb, TechnicalClimb }
/// <summary>Signed ground slope over 50 m. The two climb bands come first.</summary>
public enum GradeBand { SteepClimb, Climb, Flat, GentleDescent, Descent, SteepDescent, VerySteepDescent }
public enum RoughnessSource { Both, FrameOnly, ForkOnly }
public enum TerrainRoughnessState { Classified, ValuesOnly, NoSensor }
public enum TerrainGradeState { Available, NoFrameSensor, NoElevation, TooShort }
public enum TerrainUnavailableReason { NoTrack, NoMovement }
public enum TerrainSensor { ForkTravel, ShockTravel, FrameImu, ForkImu, RearAxleImu }

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

/// <summary>Rear pedal rhythm in one window. Cadence (rpm) and bob (rear peak-to-peak mm) exist only when detected.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public readonly record struct PedallingFigures(bool Detected, double? CadenceRpm, double? BobMillimetres);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record TerrainWindow(double StartMetres, double StartSeconds, double EndSeconds, double MeanSpeed, bool Moving)
{
    public SensorRoughness? Frame { get; init; }
    public SensorRoughness? Fork { get; init; }
    public SensorRoughness? RearAxle { get; init; }
    public RoughnessClass? Roughness { get; init; }
    public RoughnessSource? Source { get; init; }
    public double? Grade { get; init; }
    public GradeBand? Band { get; init; }

    [IgnoreMember]
    public bool Climbing => Band?.IsClimb() == true;

    [IgnoreMember]
    public double? NormalisedRoughness => Source switch
    {
        RoughnessSource.Both => (Frame?.Normalised + Fork?.Normalised) / 2,
        RoughnessSource.FrameOnly => Frame?.Normalised,
        RoughnessSource.ForkOnly => Fork?.Normalised,
        _ => null
    };
    public SuspensionWindowFigures? Front { get; init; }
    public SuspensionWindowFigures? Rear { get; init; }

    /// <summary>Null where the rear travel or the frame IMU does not cover the 8 s around the window.</summary>
    public PedallingFigures? Pedalling { get; init; }

    /// <summary>The share of the window's time inside the range; the whole window without a range.</summary>
    internal double CoveredFraction(TelemetryTimeRange? range)
    {
        var start = Math.Max(StartSeconds, range?.StartSeconds ?? StartSeconds);
        var end = Math.Min(EndSeconds, range?.EndSeconds ?? EndSeconds);
        return end > start ? (end - start) / (EndSeconds - StartSeconds) : 0;
    }
}

[MessagePackObject(keyAsPropertyName: true)]
public readonly record struct TerrainMatchSample(double Metres, double Seconds, double LogLevel);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record TerrainProfile(ImmutableArray<TerrainWindow> Windows,
    ImmutableArray<TerrainMatchSample> FrameMatching, ImmutableArray<TerrainMatchSample> ForkMatching,
    TerrainRoughnessState RoughnessState, TerrainGradeState GradeState, double? FrameScale, double? ForkScale)
{
    /// <summary>Sensors that read zero in most measured moving windows; the profile holds no readings from them.</summary>
    public ImmutableArray<TerrainSensor> SilentSensors { get; init; } = [];
    public double? RearAxleScale { get; init; }
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
