using System;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Interactivity.UserActionResponses;

namespace Sufni.App.Views.Plots;

public class SufniAvaPlot : AvaPlot
{
    internal const double PrecisionZoomSlowdownFactor = 5.0;

    public bool IsPointInDataArea(Point point)
    {
        var dataRect = Plot.LastRender.DataRect;
        var pixel = ToScottPlotPixel(point);
        var left = Math.Min(dataRect.Left, dataRect.Right);
        var right = Math.Max(dataRect.Left, dataRect.Right);
        var top = Math.Min(dataRect.Top, dataRect.Bottom);
        var bottom = Math.Max(dataRect.Top, dataRect.Bottom);

        return pixel.X >= left && pixel.X <= right && pixel.Y >= top && pixel.Y <= bottom;
    }

    public Pixel ToScottPlotPixel(Point point)
    {
        var (scaleX, scaleY) = GetRenderScale();
        return new Pixel((float)(point.X * scaleX), (float)(point.Y * scaleY));
    }

    public ScottPlot.PixelSize GetScottPlotPixelSize()
    {
        var figureRect = Plot.LastRender.FigureRect;
        if (figureRect.HasArea)
        {
            return figureRect.Size;
        }

        return new ScottPlot.PixelSize((float)Math.Max(1, Bounds.Width), (float)Math.Max(1, Bounds.Height));
    }

    private (double X, double Y) GetRenderScale()
    {
        var figureRect = Plot.LastRender.FigureRect;
        if (!figureRect.HasArea || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return (1, 1);
        }

        return (
            Math.Abs(figureRect.Width) / Bounds.Width,
            Math.Abs(figureRect.Height) / Bounds.Height);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (!IsPointInDataArea(e.GetPosition(this)) || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            ProcessWheelWithPrecisionZoom(e);
            return;
        }

        base.OnPointerWheelChanged(e);
    }

    private void ProcessWheelWithPrecisionZoom(PointerWheelEventArgs e)
    {
        var wheelZoomResponses = UserInputProcessor.UserActionResponses.OfType<MouseWheelZoom>().ToArray();
        if (wheelZoomResponses.Length == 0)
        {
            base.OnPointerWheelChanged(e);
            return;
        }

        PlotWheelZoomModifier.RunWithPrecisionZoom(wheelZoomResponses, () => base.OnPointerWheelChanged(e));
    }
}

internal static class PlotWheelZoomModifier
{
    public static void RunWithPrecisionZoom(MouseWheelZoom[] wheelZoomResponses, Action processWheel)
    {
        var originalZoomFractions = wheelZoomResponses.Select(response => response.ZoomFraction).ToArray();
        try
        {
            for (var i = 0; i < wheelZoomResponses.Length; i++)
            {
                wheelZoomResponses[i].ZoomFraction = originalZoomFractions[i] / SufniAvaPlot.PrecisionZoomSlowdownFactor;
            }

            processWheel();
        }
        finally
        {
            for (var i = 0; i < wheelZoomResponses.Length; i++)
            {
                wheelZoomResponses[i].ZoomFraction = originalZoomFractions[i];
            }
        }
    }
}
