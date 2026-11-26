using System.Collections.Immutable;
using MessagePack;

namespace Sufni.Telemetry;

/// <summary>Five levels on the speed-corrected descent scale, then two climb levels from frame vibration.</summary>
public enum RoughnessClass { Smooth, Calm, Medium, Rough, VeryRough, SmoothClimb, TechnicalClimb }
/// <summary>Band of the signed baseline grade, over 30 m to 50 m. The two climb bands come first.</summary>
public enum GradeBand { SteepClimb, Climb, Flat, GentleDescent, Descent, SteepDescent, VerySteepDescent }
/// <summary>The IMUs that read the ground for a response fit, unsprung first.</summary>
public enum GroundSensors { ForkAndRearAxle, Fork, RearAxle, Frame }
public enum TerrainGradeState { Available, NoFrameSensor, NoElevation, TooShort }
public enum TerrainUnavailableReason { NoTrack, NoMovement }
public enum TerrainSensor { ForkTravel, ShockTravel, FrameImu, ForkImu, RearAxleImu }

[MessagePackObject(keyAsPropertyName: true)]
public sealed record SensorRoughness(double Level, double LowBand, double MiddleBand, double HighBand,
    double SpeedRemoved, double ClippedShare);

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
    public double? Grade { get; init; }
    public GradeBand? Band { get; init; }

    [IgnoreMember]
    public bool Climbing => Band?.IsClimb() == true;

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
    TerrainGradeState GradeState, double? FrameScale, double? ForkScale)
{
    /// <summary>Sensors that read zero in most measured moving windows; the profile holds no readings from them.</summary>
    public ImmutableArray<TerrainSensor> SilentSensors { get; init; } = [];
    public double? RearAxleScale { get; init; }
    public double? FrontMaxTravel { get; init; }
    public double? RearMaxTravel { get; init; }

    [IgnoreMember]
    public bool HasImu => FrameScale is not null || ForkScale is not null || RearAxleScale is not null;
}

public closed record TerrainProfileResult
{
    private TerrainProfileResult() { }
    public sealed record Computed(TerrainProfile Profile) : TerrainProfileResult;
    public sealed record Unavailable(TerrainUnavailableReason Reason) : TerrainProfileResult;
}

/// <summary>
/// Ride totals for the session list and the setup scale. <see cref="ClimbMetres"/> is null without grade.
/// The distributions hold the unclipped readings above zero: the speed-removed levels of each IMU on descents and the
/// raw frame level on climbs. A distribution is null without such a reading.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record TerrainSummary(double MovingMetres, double? ClimbMetres, LevelDistribution? FrameImu = null,
    LevelDistribution? ForkImu = null, LevelDistribution? RearAxleImu = null, LevelDistribution? FrameClimb = null);

/// <summary>Moving distance in metres per <see cref="TerrainLevels.Bin"/> of a reading level.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LevelDistribution(ImmutableSortedDictionary<int, double> MetresByBin)
{
    public bool Equals(LevelDistribution? other) => other is not null && MetresByBin.SequenceEqual(other.MetresByBin);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var (bin, metres) in MetresByBin)
        {
            hash.Add(bin);
            hash.Add(metres);
        }
        return hash.ToHashCode();
    }
}
