using System;

namespace Sufni.App.Views.Plots;

/// <summary>
/// Drag/touch gesture tunables for damping-cutoff editing. The value policy
/// (drag step and rounding) lives on
/// <see cref="Sufni.App.SessionDetails.DampingCutoffEditing"/>; these values
/// shape the app's pointer interaction only.
/// </summary>
public static class DampingCutoffInteraction
{
    public const int MobileLongPressDelayMilliseconds = 250;

    public static TimeSpan MobileLongPressDelay { get; } = TimeSpan.FromMilliseconds(MobileLongPressDelayMilliseconds);
}
