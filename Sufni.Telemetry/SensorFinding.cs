using System.Collections.Immutable;
using System.Text.Json.Serialization;
using MessagePack;

namespace Sufni.Telemetry;

public enum SensorKind : byte
{
    ForkTravel = 0,
    ShockTravel = 1,
    Imu = 2,
    Gps = 3,
    Temperature = 4,
}

/// <summary>One sensor of a recording.</summary>
/// <param name="Location">The IMU or temperature location. Null for the other kinds.</param>
[MessagePackObject(keyAsPropertyName: true)]
public readonly record struct SensorIdentity(SensorKind Kind, byte? Location = null);

public enum SensorFaultReason : byte
{
    OutOfRange = 0,
    Frozen = 1,
    Zero = 2,
    Glitch = 3,
    JumpAndStuck = 4,
    FixLost = 5,
    NoValue = 6,
}

/// <summary>A half-open interval <c>[Start, End)</c> in seconds on the recording source timeline.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public readonly record struct SourceTimeRange(double StartSeconds, double EndSeconds)
{
    [IgnoreMember, JsonIgnore] public double DurationSeconds => EndSeconds - StartSeconds;
}

/// <summary>Ranges in which one sensor gave wrong readings, or none.</summary>
/// <param name="Ranges">Ordered, disjoint ranges on the recording source timeline.</param>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record SensorFinding(SensorIdentity Sensor, SensorFaultReason Reason, ImmutableArray<SourceTimeRange> Ranges)
{
    /// <summary>Missing readings. A notice has nothing to drop, so it takes no decision.</summary>
    [IgnoreMember, JsonIgnore] public bool IsNotice => Reason is SensorFaultReason.FixLost or SensorFaultReason.NoValue;

    public bool Equals(SensorFinding? other) =>
        other is not null && Sensor == other.Sensor && Reason == other.Reason && Ranges.SequenceEqual(other.Ranges);

    public override int GetHashCode() => HashCode.Combine(Sensor, Reason, Ranges.Length);
}
