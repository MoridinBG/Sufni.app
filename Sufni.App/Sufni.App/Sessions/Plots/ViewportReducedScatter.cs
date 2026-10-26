using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using ScottPlot.DataSources;
using ScottPlot.Plottables;
using SkiaSharp;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Plots;

internal sealed class ViewportReducedScatter : Scatter, IDisposable
{
    private static readonly double[] BaseErrorTargets =
        [0, 0.05, 0.10, 0.15, 0.20, 0.25, 0.30, 0.40, 0.50];

    private readonly PositionVelocitySideTrace side;
    private readonly SKPath pixelPath = new();
    private PathLevel[] levels = [];
    private float levelWidth;
    private float levelHeight;
    private double fullXMinimum;
    private double fullXSpan;
    private double fullYMinimum;
    private double fullYSpan;
    private bool disposed;

    internal ViewportReducedScatter(PositionVelocitySideTrace side)
        : base(new ScatterSourceCoordinatesArray(Flatten(side)))
    {
        this.side = side;
        RenderedCoordinateCount = side.Strokes.Sum(stroke => stroke.Count);
    }

    internal int RenderedCoordinateCount { get; private set; }

    internal double CurrentScreenSpaceErrorPixels { get; private set; }

    internal bool IsDisposed => disposed;

    public override void Render(RenderPack renderPack)
    {
        if (disposed || !IsVisible || !LineStyle.IsVisible)
        {
            return;
        }

        EnsureLevels(renderPack.DataRect);
        var level = SelectLevel();
        RenderedCoordinateCount = level.CoordinateCount;

        var origin = Axes.GetPixel(new Coordinates(0, 0));
        var xUnit = Axes.GetPixel(new Coordinates(1, 0));
        var yUnit = Axes.GetPixel(new Coordinates(0, 1));
        var transform = SKMatrix.CreateScaleTranslation(
            xUnit.X - origin.X,
            yUnit.Y - origin.Y,
            origin.X,
            origin.Y);
        foreach (var coordinatePath in level.StrokePaths)
        {
            pixelPath.Rewind();
            coordinatePath.Transform(transform, pixelPath);
            LineStyle.Render(renderPack.Canvas, pixelPath, renderPack.Paint);
        }
    }

    private void EnsureLevels(PixelRect dataRect)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (levels.Length != 0 && levelWidth == dataRect.Width && levelHeight == dataRect.Height)
        {
            return;
        }

        if (levels.Length == 0)
        {
            fullXMinimum = Axes.XAxis.Min;
            fullXSpan = Axes.XAxis.Max - Axes.XAxis.Min;
            fullYMinimum = Axes.YAxis.Min;
            fullYSpan = Axes.YAxis.Max - Axes.YAxis.Min;
        }

        var replacements = new List<PathLevel>(BaseErrorTargets.Length);
        foreach (var errorTarget in BaseErrorTargets)
        {
            replacements.Add(BuildLevel(errorTarget, dataRect.Width, dataRect.Height));
        }

        var replacementLevels = replacements.ToArray();
        var previousLevels = levels;
        levels = replacementLevels;
        levelWidth = dataRect.Width;
        levelHeight = dataRect.Height;
        DisposeLevels(previousLevels);
    }

    private PathLevel SelectLevel()
    {
        var xSpan = Math.Abs(Axes.XAxis.Max - Axes.XAxis.Min);
        var ySpan = Math.Abs(Axes.YAxis.Max - Axes.YAxis.Min);
        if (!double.IsFinite(xSpan) || xSpan <= 0 || !double.IsFinite(ySpan) || ySpan <= 0)
        {
            CurrentScreenSpaceErrorPixels = levels[0].MaximumBaseErrorPixels;
            return levels[0];
        }

        var maximumScale = Math.Max(Math.Abs(fullXSpan) / xSpan, Math.Abs(fullYSpan) / ySpan);
        var selected = levels[0];
        var selectedError = selected.MaximumBaseErrorPixels * maximumScale;
        foreach (var level in levels)
        {
            var screenError = level.MaximumBaseErrorPixels * maximumScale;
            if (screenError <= 0.5)
            {
                selected = level;
                selectedError = screenError;
            }
        }

        CurrentScreenSpaceErrorPixels = selectedError;
        return selected;
    }

    private PathLevel BuildLevel(double errorTarget, float width, float height)
    {
        var strokePaths = new SKPath[side.Strokes.Count];
        var maximumStrokeCount = side.Strokes.Max(stroke => stroke.Count);
        var originalPixels = new ScreenPoint[maximumStrokeCount];
        var pathPixels = new ScreenPoint[maximumStrokeCount];
        var keep = new bool[maximumStrokeCount];
        var stackStarts = new int[maximumStrokeCount];
        var stackEnds = new int[maximumStrokeCount];
        var coordinateCount = 0;
        var maximumErrorSquared = 0.0;

        for (var strokeIndex = 0; strokeIndex < side.Strokes.Count; strokeIndex++)
        {
            var path = new SKPath();
            strokePaths[strokeIndex] = path;
            var stroke = side.Strokes[strokeIndex];
            var count = stroke.Count;
            for (var index = 0; index < count; index++)
            {
                var x = stroke.PositionPercent[index];
                var y = stroke.VelocityMmPerSecond[index];
                originalPixels[index] = ToBasePixel(x, y, width, height);
                pathPixels[index] = ToBasePixel((float)x, (float)y, width, height);
            }

            SelectInteriorPoints(originalPixels, count, errorTarget, keep, stackStarts, stackEnds);
            var first = true;
            var previousKeptIndex = -1;
            for (var index = 0; index < count; index++)
            {
                if (!keep[index])
                {
                    continue;
                }

                if (first)
                {
                    path.MoveTo(
                        (float)stroke.PositionPercent[index],
                        (float)stroke.VelocityMmPerSecond[index]);
                    first = false;
                }
                else
                {
                    path.LineTo(
                        (float)stroke.PositionPercent[index],
                        (float)stroke.VelocityMmPerSecond[index]);
                }

                if (previousKeptIndex >= 0)
                {
                    for (var sourceIndex = previousKeptIndex; sourceIndex <= index; sourceIndex++)
                    {
                        maximumErrorSquared = Math.Max(
                            maximumErrorSquared,
                            SegmentDistanceSquared(
                                originalPixels[sourceIndex],
                                pathPixels[previousKeptIndex],
                                pathPixels[index]));
                    }
                }
                previousKeptIndex = index;
                coordinateCount++;
            }

            if (count == 1)
            {
                maximumErrorSquared = Math.Max(
                    maximumErrorSquared,
                    PointDistanceSquared(originalPixels[0], pathPixels[0]));
            }
        }

        return new PathLevel(strokePaths, coordinateCount, Math.Sqrt(maximumErrorSquared));
    }

    private static void SelectInteriorPoints(
        ScreenPoint[] pixels,
        int count,
        double errorTarget,
        bool[] keep,
        int[] stackStarts,
        int[] stackEnds)
    {
        Array.Clear(keep, 0, count);
        keep[0] = true;
        if (count == 1)
        {
            return;
        }
        keep[count - 1] = true;
        if (errorTarget <= 0 || count == 2)
        {
            Array.Fill(keep, true, 0, count);
            return;
        }

        var errorSquared = errorTarget * errorTarget;
        var stackCount = 1;
        stackStarts[0] = 0;
        stackEnds[0] = count - 1;
        while (stackCount > 0)
        {
            stackCount--;
            var start = stackStarts[stackCount];
            var end = stackEnds[stackCount];
            var farthestIndex = -1;
            var farthestDistanceSquared = errorSquared;
            for (var index = start + 1; index < end; index++)
            {
                var distanceSquared = SegmentDistanceSquared(pixels[index], pixels[start], pixels[end]);
                if (distanceSquared > farthestDistanceSquared)
                {
                    farthestDistanceSquared = distanceSquared;
                    farthestIndex = index;
                }
            }

            if (farthestIndex < 0)
            {
                continue;
            }

            keep[farthestIndex] = true;
            stackStarts[stackCount] = start;
            stackEnds[stackCount] = farthestIndex;
            stackCount++;
            stackStarts[stackCount] = farthestIndex;
            stackEnds[stackCount] = end;
            stackCount++;
        }
    }

    private ScreenPoint ToBasePixel(double x, double y, float width, float height) =>
        new(
            (x - fullXMinimum) / fullXSpan * width,
            (y - fullYMinimum) / fullYSpan * height);

    private static double SegmentDistanceSquared(ScreenPoint point, ScreenPoint start, ScreenPoint end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var lengthSquared = deltaX * deltaX + deltaY * deltaY;
        if (lengthSquared <= 0)
        {
            return PointDistanceSquared(point, start);
        }

        var projection = Math.Clamp(
            ((point.X - start.X) * deltaX + (point.Y - start.Y) * deltaY) / lengthSquared,
            0,
            1);
        var closest = new ScreenPoint(start.X + projection * deltaX, start.Y + projection * deltaY);
        return PointDistanceSquared(point, closest);
    }

    private static double PointDistanceSquared(ScreenPoint first, ScreenPoint second)
    {
        var deltaX = first.X - second.X;
        var deltaY = first.Y - second.Y;
        return deltaX * deltaX + deltaY * deltaY;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        pixelPath.Dispose();
        DisposeLevels(levels);
        levels = [];
    }

    private static void DisposeLevels(IEnumerable<PathLevel> pathLevels)
    {
        foreach (var level in pathLevels)
        {
            foreach (var strokePath in level.StrokePaths)
            {
                strokePath.Dispose();
            }
        }
    }

    private static Coordinates[] Flatten(PositionVelocitySideTrace side)
    {
        var valueCount = checked(side.Strokes.Sum(stroke => stroke.Count) + side.Strokes.Count - 1);
        var coordinates = new Coordinates[valueCount];
        var outputIndex = 0;
        for (var strokeIndex = 0; strokeIndex < side.Strokes.Count; strokeIndex++)
        {
            if (strokeIndex > 0)
            {
                coordinates[outputIndex++] = new Coordinates(double.NaN, double.NaN);
            }
            var stroke = side.Strokes[strokeIndex];
            for (var index = 0; index < stroke.Count; index++)
            {
                coordinates[outputIndex++] = new Coordinates(
                    stroke.PositionPercent[index],
                    stroke.VelocityMmPerSecond[index]);
            }
        }
        return coordinates;
    }

    private readonly record struct ScreenPoint(double X, double Y);

    private sealed record PathLevel(
        IReadOnlyList<SKPath> StrokePaths,
        int CoordinateCount,
        double MaximumBaseErrorPixels);
}
