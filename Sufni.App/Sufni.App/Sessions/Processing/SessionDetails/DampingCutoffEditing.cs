using System;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

namespace Sufni.App.Sessions.Processing.SessionDetails;

/// <summary>
/// Value policy for damping-cutoff editing: the drag step and the
/// step-rounding rule shared by workflows, coordinators, and plot views. The
/// domain bounds (<c>Default</c>/<c>Minimum</c>/<c>Maximum</c>/<c>Clamp</c>)
/// stay on the SDK's <see cref="DampingSpeedCutoffs"/> record.
/// </summary>
public static class DampingCutoffEditing
{
    public const double DragStepMmPerSecond = 10.0;

    public static double RoundDragValue(double value)
    {
        var clamped = DampingSpeedCutoffs.Clamp(value);
        return DampingSpeedCutoffs.Clamp(
            Math.Round(clamped / DragStepMmPerSecond, MidpointRounding.AwayFromZero) * DragStepMmPerSecond);
    }
}
