using System;
using Avalonia.Media;

namespace Sufni.App.Theming;

// Pure-Avalonia alpha helper used by the theme model snapshots (which now live in
// the SDK). Kept separate from the app-side SufniColorExtensions so the SDK does
// not pull in ScottPlot. App theming code resolves it through the same namespace.
public static class SufniColorAlphaExtensions
{
    extension(Color color)
    {
        public Color WithAlpha(double alpha)
            => Color.FromArgb(
                (byte)(Math.Clamp(alpha, 0, 1) * byte.MaxValue),
                color.R,
                color.G,
                color.B);
    }
}
