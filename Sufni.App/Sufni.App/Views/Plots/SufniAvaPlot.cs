using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
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
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (TryScrollAncestor(e))
            {
                e.Handled = true;
            }

            return;
        }

        if (!IsPointInDataArea(e.GetPosition(this)))
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

    private bool TryScrollAncestor(PointerWheelEventArgs e)
    {
        var deltaY = GetVerticalScrollDelta(e.Delta);
        if (deltaY == 0)
        {
            return false;
        }

        var scrollViewer = this.GetVisualAncestors()
            .OfType<ScrollViewer>()
            .FirstOrDefault(candidate => CanScrollVertically(candidate, deltaY));
        if (scrollViewer is null)
        {
            return false;
        }

        var previousOffset = scrollViewer.Offset;
        var forwardedArgs = CreateForwardedWheelArgs(e, scrollViewer, deltaY);
        scrollViewer.RaiseEvent(forwardedArgs);

        if (forwardedArgs.Handled || HasVerticalOffsetChanged(scrollViewer, previousOffset))
        {
            return true;
        }

        return ScrollDirectly(scrollViewer, deltaY);
    }

    private static double GetVerticalScrollDelta(Vector delta)
    {
        if (delta.Y != 0)
        {
            return delta.Y;
        }

        return delta.X;
    }

    private static PointerWheelEventArgs CreateForwardedWheelArgs(
        PointerWheelEventArgs original,
        ScrollViewer scrollViewer,
        double deltaY)
    {
        var keyModifiers = original.KeyModifiers & ~KeyModifiers.Shift;
        return new PointerWheelEventArgs(
            scrollViewer,
            original.Pointer,
            scrollViewer,
            original.GetPosition(scrollViewer),
            original.Timestamp,
            new PointerPointProperties(ToRawInputModifiers(keyModifiers), PointerUpdateKind.Other),
            keyModifiers,
            new Vector(0, deltaY));
    }

    private static RawInputModifiers ToRawInputModifiers(KeyModifiers keyModifiers)
    {
        var rawModifiers = RawInputModifiers.None;
        if (keyModifiers.HasFlag(KeyModifiers.Alt))
        {
            rawModifiers |= RawInputModifiers.Alt;
        }

        if (keyModifiers.HasFlag(KeyModifiers.Control))
        {
            rawModifiers |= RawInputModifiers.Control;
        }

        if (keyModifiers.HasFlag(KeyModifiers.Meta))
        {
            rawModifiers |= RawInputModifiers.Meta;
        }

        return rawModifiers;
    }

    private static bool CanScrollVertically(ScrollViewer scrollViewer, double deltaY)
    {
        var maxOffset = GetMaxVerticalOffset(scrollViewer);
        if (maxOffset <= 0)
        {
            return false;
        }

        return deltaY < 0
            ? scrollViewer.Offset.Y < maxOffset
            : scrollViewer.Offset.Y > 0;
    }

    private static bool ScrollDirectly(ScrollViewer scrollViewer, double deltaY)
    {
        var previousOffset = scrollViewer.Offset;
        var maxOffset = GetMaxVerticalOffset(scrollViewer);
        var smallChange = scrollViewer.SmallChange.Height;
        if (double.IsNaN(smallChange) || smallChange <= 0)
        {
            smallChange = 50;
        }

        var nextY = Math.Clamp(previousOffset.Y - deltaY * smallChange, 0, maxOffset);
        if (Math.Abs(nextY - previousOffset.Y) < 0.001)
        {
            return false;
        }

        scrollViewer.Offset = new Vector(previousOffset.X, nextY);
        return true;
    }

    private static double GetMaxVerticalOffset(ScrollViewer scrollViewer) =>
        Math.Max(0, Math.Max(scrollViewer.ScrollBarMaximum.Y, scrollViewer.Extent.Height - scrollViewer.Viewport.Height));

    private static bool HasVerticalOffsetChanged(ScrollViewer scrollViewer, Vector previousOffset) =>
        Math.Abs(scrollViewer.Offset.Y - previousOffset.Y) >= 0.001;

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
