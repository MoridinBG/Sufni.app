using System;
using Sufni.Telemetry;

namespace Sufni.App.SessionDetails;

public enum DampingSpeedCircuit
{
    Compression,
    Rebound,
}

public sealed record DampingSpeedCutoffSide(
    double CompressionMmPerSecond,
    double ReboundMmPerSecond)
{
    public static DampingSpeedCutoffSide Default { get; } = new(
        DampingSpeedCutoffs.DefaultMmPerSecond,
        DampingSpeedCutoffs.DefaultMmPerSecond);

    public double Get(DampingSpeedCircuit circuit)
    {
        return circuit switch
        {
            DampingSpeedCircuit.Compression => CompressionMmPerSecond,
            DampingSpeedCircuit.Rebound => ReboundMmPerSecond,
            _ => throw new ArgumentOutOfRangeException(nameof(circuit), circuit, null),
        };
    }

    public DampingSpeedCutoffSide With(DampingSpeedCircuit circuit, double value)
    {
        var clamped = DampingSpeedCutoffs.Clamp(value);
        return circuit switch
        {
            DampingSpeedCircuit.Compression => this with { CompressionMmPerSecond = clamped },
            DampingSpeedCircuit.Rebound => this with { ReboundMmPerSecond = clamped },
            _ => throw new ArgumentOutOfRangeException(nameof(circuit), circuit, null),
        };
    }

    public DampingSpeedCutoffSide ClampValues() => new(
        DampingSpeedCutoffs.Clamp(CompressionMmPerSecond),
        DampingSpeedCutoffs.Clamp(ReboundMmPerSecond));
}

public sealed record DampingSpeedCutoffs(
    DampingSpeedCutoffSide Front,
    DampingSpeedCutoffSide Rear)
{
    public const double DefaultMmPerSecond = 200.0;
    public const double MinimumMmPerSecond = 0.0;
    public const double MaximumMmPerSecond = 2000.0;
    public const double DragStepMmPerSecond = 10.0;
    public const int MobileLongPressDelayMilliseconds = 250;

    public static TimeSpan MobileLongPressDelay { get; } = TimeSpan.FromMilliseconds(MobileLongPressDelayMilliseconds);

    public static DampingSpeedCutoffs Default { get; } = new(
        DampingSpeedCutoffSide.Default,
        DampingSpeedCutoffSide.Default);

    public static DampingSpeedCutoffs FromValues(
        double frontCompressionMmPerSecond,
        double frontReboundMmPerSecond,
        double rearCompressionMmPerSecond,
        double rearReboundMmPerSecond)
    {
        return new DampingSpeedCutoffs(
            new DampingSpeedCutoffSide(frontCompressionMmPerSecond, frontReboundMmPerSecond),
            new DampingSpeedCutoffSide(rearCompressionMmPerSecond, rearReboundMmPerSecond))
            .ClampValues();
    }

    public DampingSpeedCutoffSide ForSide(SuspensionType side)
    {
        return side switch
        {
            SuspensionType.Front => Front,
            SuspensionType.Rear => Rear,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
        };
    }

    public double Get(SuspensionType side, DampingSpeedCircuit circuit)
    {
        return ForSide(side).Get(circuit);
    }

    public DampingSpeedCutoffs With(SuspensionType side, DampingSpeedCircuit circuit, double value)
    {
        return side switch
        {
            SuspensionType.Front => this with { Front = Front.With(circuit, value) },
            SuspensionType.Rear => this with { Rear = Rear.With(circuit, value) },
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
        };
    }

    public DampingSpeedCutoffs ClampValues() => new(
        Front.ClampValues(),
        Rear.ClampValues());

    public static double Clamp(double value)
    {
        return Math.Clamp(value, MinimumMmPerSecond, MaximumMmPerSecond);
    }

    public static double RoundDragValue(double value)
    {
        var clamped = Clamp(value);
        return Clamp(Math.Round(clamped / DragStepMmPerSecond, MidpointRounding.AwayFromZero) * DragStepMmPerSecond);
    }
}
