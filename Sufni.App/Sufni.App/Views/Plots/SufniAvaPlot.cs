using System;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using ScottPlot.Avalonia;
using ScottPlot.Interactivity.UserActionResponses;

namespace Sufni.App.Views.Plots;

public class SufniAvaPlot : AvaPlot
{
    internal const double PrecisionZoomSlowdownFactor = 5.0;

    public bool IsPointInDataArea(Point point)
    {
        var dataRect = Plot.LastRender.DataRect;
        var left = Math.Min(dataRect.Left, dataRect.Right);
        var right = Math.Max(dataRect.Left, dataRect.Right);
        var top = Math.Min(dataRect.Top, dataRect.Bottom);
        var bottom = Math.Max(dataRect.Top, dataRect.Bottom);

        return point.X >= left && point.X <= right && point.Y >= top && point.Y <= bottom;
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
