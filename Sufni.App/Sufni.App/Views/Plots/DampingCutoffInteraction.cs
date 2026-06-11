using System;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

namespace Sufni.App.Views.Plots;

/// <summary>
/// Drag/touch interaction tunables for damping-cutoff editing. The domain
/// bounds (<c>Default</c>/<c>Minimum</c>/<c>Maximum</c>/<c>Clamp</c>) stay on
/// the SDK's <see cref="DampingSpeedCutoffs"/> record; these values shape the
/// app's pointer interaction only.
/// </summary>
public static class DampingCutoffInteraction
{
    public const double DragStepMmPerSecond = 10.0;
    public const int MobileLongPressDelayMilliseconds = 250;

    public static TimeSpan MobileLongPressDelay { get; } = TimeSpan.FromMilliseconds(MobileLongPressDelayMilliseconds);

    public static double RoundDragValue(double value)
    {
        var clamped = DampingSpeedCutoffs.Clamp(value);
        return DampingSpeedCutoffs.Clamp(
            Math.Round(clamped / DragStepMmPerSecond, MidpointRounding.AwayFromZero) * DragStepMmPerSecond);
    }
}
