namespace Sufni.Kinematics;

public enum KinematicCalculationFailureReason
{
    MissingJointPath,
    DegenerateGeometry
}

/// <summary>A geometry or solution input cannot support the requested calculation.</summary>
public sealed class KinematicCalculationException(KinematicCalculationFailureReason reason, string message) : Exception(message)
{
    public KinematicCalculationFailureReason Reason { get; } = reason;
}
