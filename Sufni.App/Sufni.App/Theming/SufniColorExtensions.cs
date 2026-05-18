using System;
using Avalonia.Media;
using AvaloniaColor = Avalonia.Media.Color;
using ScottPlotColor = ScottPlot.Color;

namespace Sufni.App.Theming;

public static class SufniColorExtensions
{
    extension(AvaloniaColor color)
    {
        public AvaloniaColor WithAlpha(double alpha)
            => AvaloniaColor.FromArgb(
                (byte)(Math.Clamp(alpha, 0, 1) * byte.MaxValue),
                color.R,
                color.G,
                color.B);

        public ScottPlotColor ToScottPlotColor()
        {
            var plotColor = ScottPlotColor.FromHex($"#{color.R:x2}{color.G:x2}{color.B:x2}");
            return color.A == byte.MaxValue
                ? plotColor
                : plotColor.WithAlpha(color.A / (double)byte.MaxValue);
        }

        public IBrush ToBrush()
            => new SolidColorBrush(color);

        public string ToHexString()
            => color.A == byte.MaxValue
                ? $"#{color.R:x2}{color.G:x2}{color.B:x2}"
                : $"#{color.A:x2}{color.R:x2}{color.G:x2}{color.B:x2}";
    }
}
