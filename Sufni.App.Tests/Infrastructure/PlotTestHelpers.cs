using Avalonia;
using ScottPlot;
using ScottPlot.Plottables;

namespace Sufni.App.Tests.Infrastructure;

public static class PlotTestHelpers
{
    public static IEnumerable<string> ReadTextLabels(Text text)
    {
        return text.GetType()
            .GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.GetValue(text) as string)
            .Where(label => !string.IsNullOrWhiteSpace(label))!;
    }

    public static Text[] GetAirtimeLabels(Plot plot)
    {
        return plot.PlottableList
            .OfType<Text>()
            .Where(text => ReadTextLabels(text).Any(label => label.Contains("s air")))
            .ToArray();
    }

    public static Pixel GetLegendItemCenter(Plot plot, IPlottable plottable, ScottPlot.PixelSize plotSize)
    {
        plot.RenderInMemory((int)plotSize.Width, (int)plotSize.Height);
        using var paint = Paint.NewDisposablePaint();
        var dataRect = plot.LastRender.DataRect.HasArea
            ? plot.LastRender.DataRect
            : plotSize.ToPixelRect();
        var layout = plot.Legend.GetLayout(dataRect.Size, paint);
        var legendRect = layout.LegendRect.AlignedInside(dataRect, plot.Legend.Alignment);
        var itemCount = layout.LegendItems.Length;

        for (var index = 0; index < itemCount; index++)
        {
            if (!ReferenceEquals(layout.LegendItems[index].Plottable, plottable))
            {
                continue;
            }

            var rowHeight = legendRect.Height / itemCount;
            return new Pixel(
                (legendRect.Left + legendRect.Right) / 2,
                legendRect.Top + rowHeight * index + rowHeight / 2);
        }

        throw new InvalidOperationException("Legend item was not found.");
    }

    public static Point GetLegendItemCenter(ScottPlot.Avalonia.AvaPlot plot, IPlottable plottable)
    {
        var plotSize = new ScottPlot.PixelSize((float)plot.Bounds.Width, (float)plot.Bounds.Height);
        var pixel = GetLegendItemCenter(plot.Plot, plottable, plotSize);
        return new Point(pixel.X, pixel.Y);
    }

    public static void AssertAxisLimitsEqual(AxisLimits expected, AxisLimits actual)
    {
        Assert.Equal(expected.Left, actual.Left, precision: 8);
        Assert.Equal(expected.Right, actual.Right, precision: 8);
        Assert.Equal(expected.Bottom, actual.Bottom, precision: 8);
        Assert.Equal(expected.Top, actual.Top, precision: 8);
    }
}
