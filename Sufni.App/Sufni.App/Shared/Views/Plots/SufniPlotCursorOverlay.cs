using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Sufni.App.Shared.Views.Plots;

internal sealed class SufniPlotCursorOverlay : Control
{
    private const double CursorLineWidthPixels = 1;

    private SufniAvaPlot? plotControl;
    private double cursorPosition = double.NaN;
    private Color cursorColor;
    private Pen? cursorPen;
    private double cursorPenThickness;

    internal double CursorPosition => cursorPosition;

    internal bool Update(SufniAvaPlot plot, double position, Color color)
    {
        plotControl = plot;
        cursorPosition = position;
        cursorColor = color;
        InvalidateVisual();

        return CanUseOverlay();
    }

    internal void Clear()
    {
        plotControl = null;
        cursorPosition = double.NaN;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!TryGetCursorLineGeometry(out var start, out var end))
        {
            return;
        }

        var (scaleX, _) = plotControl!.GetRenderScale();
        var penThickness = CursorLineWidthPixels / scaleX;

        if (cursorPen is null || cursorPenThickness != penThickness || cursorPen.Brush is not SolidColorBrush brush || brush.Color != cursorColor)
        {
            cursorPenThickness = penThickness;
            cursorPen = new Pen(new SolidColorBrush(cursorColor), penThickness);
        }

        context.DrawLine(cursorPen, start, end);
    }

    private bool TryGetCursorLineGeometry(out Point start, out Point end)
    {
        start = default;
        end = default;
        if (!CanUseOverlay() || !double.IsFinite(cursorPosition) || plotControl is not { } plot)
        {
            return false;
        }

        var dataRect = plot.Plot.LastRender.DataRect;
        var (scaleX, scaleY) = plot.GetRenderScale();
        var x = plot.Plot.Axes.Bottom.GetPixel(cursorPosition, dataRect) / scaleX;
        start = new Point(x, dataRect.Top / scaleY);
        end = new Point(x, dataRect.Bottom / scaleY);
        return true;
    }

    private bool CanUseOverlay()
    {
        return plotControl is { Bounds.Width: > 0, Bounds.Height: > 0 } plot &&
               plot.Plot.LastRender.DataRect.HasArea;
    }
}
