using Avalonia.Media;
using AvaloniaColor = Avalonia.Media.Color;
using ScottPlotColor = ScottPlot.Color;
using Sufni.App.ExtensionHost.Contracts.Plots;

namespace Sufni.App.Infrastructure.Theming;

public static class SufniColorExtensions
{
    extension(AvaloniaColor color)
    {
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

        public RecordedTimeRangeOverlayColor ToRecordedTimeRangeOverlayColor()
            => new(color.A, color.R, color.G, color.B);
    }

    extension(RecordedTimeRangeOverlayColor color)
    {
        public ScottPlotColor ToScottPlotColor()
        {
            var plotColor = ScottPlotColor.FromHex($"#{color.R:x2}{color.G:x2}{color.B:x2}");
            return color.A == byte.MaxValue
                ? plotColor
                : plotColor.WithAlpha(color.A / (double)byte.MaxValue);
        }
    }
}
