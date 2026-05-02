using System;
using Avalonia;
using Avalonia.Input;
using ScottPlot.Avalonia;

namespace Sufni.App.Views.Plots;

public class SufniAvaPlot : AvaPlot
{
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
        if (!IsPointInDataArea(e.GetPosition(this)))
        {
            return;
        }

        base.OnPointerWheelChanged(e);
    }
}
