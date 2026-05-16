using System;
using System.Collections.Generic;
using ScottPlot;

namespace Sufni.App.Plots;

internal sealed class StatisticsBarReadout : IPointerReadoutTarget
{
    private StatisticsBarReadout(
        double position,
        double valueBase,
        double value,
        double size,
        Orientation orientation,
        string header,
        IReadOnlyList<CursorReadoutLine> lines)
    {
        Position = position;
        ValueBase = valueBase;
        Value = value;
        Size = size;
        Orientation = orientation;
        Header = header;
        Lines = lines;
    }

    private double Position { get; }
    private double ValueBase { get; }
    private double Value { get; }
    private double Size { get; }
    private Orientation Orientation { get; }
    private string Header { get; }
    private IReadOnlyList<CursorReadoutLine> Lines { get; }

    public static StatisticsBarReadout FromBar(
        Bar bar,
        string header,
        IReadOnlyList<CursorReadoutLine> lines)
    {
        return new StatisticsBarReadout(
            bar.Position,
            bar.ValueBase,
            bar.Value,
            bar.Size,
            bar.Orientation,
            header,
            lines);
    }

    public double GetDistanceSquared(Coordinates pointer, Plot plot)
    {
        var nearest = GetNearestPoint(pointer);
        var dataRect = plot.LastRender.DataRect;
        if (dataRect.Width > 0 && dataRect.Height > 0)
        {
            var pointerPixel = plot.GetPixel(pointer);
            var nearestPixel = plot.GetPixel(nearest);
            var dx = pointerPixel.X - nearestPixel.X;
            var dy = pointerPixel.Y - nearestPixel.Y;
            return dx * dx + dy * dy;
        }

        var (left, right, bottom, top) = GetBounds();
        var xSpan = Math.Max(Math.Abs(right - left), 1.0);
        var ySpan = Math.Max(Math.Abs(top - bottom), 1.0);
        var dataDx = (pointer.X - nearest.X) / xSpan;
        var dataDy = (pointer.Y - nearest.Y) / ySpan;
        return dataDx * dataDx + dataDy * dataDy;
    }

    public CursorReadout ToCursorReadout(Coordinates pointer, Plot plot)
    {
        return new CursorReadout(
            double.NaN,
            pointer.X,
            pointer.Y,
            Lines,
            Header,
            KeepTooltipInsideDataArea: true);
    }

    private Coordinates GetNearestPoint(Coordinates pointer)
    {
        var (left, right, bottom, top) = GetBounds();
        return new Coordinates(
            Math.Clamp(pointer.X, left, right),
            Math.Clamp(pointer.Y, bottom, top));
    }

    private (double Left, double Right, double Bottom, double Top) GetBounds()
    {
        var halfSize = Size / 2.0;
        if (Orientation == Orientation.Horizontal)
        {
            return (
                Math.Min(ValueBase, Value),
                Math.Max(ValueBase, Value),
                Position - halfSize,
                Position + halfSize);
        }

        return (
            Position - halfSize,
            Position + halfSize,
            Math.Min(ValueBase, Value),
            Math.Max(ValueBase, Value));
    }
}
