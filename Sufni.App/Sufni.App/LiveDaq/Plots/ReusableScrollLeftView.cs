using System;
using System.Collections.Generic;
using ScottPlot;
using ScottPlot.DataViews;
using ScottPlot.Plottables;
using SkiaSharp;

namespace Sufni.App.LiveDaq.Plots;

internal sealed class ReusableScrollLeftView(DataStreamer streamer) : IDataStreamerView
{
    private Pixel[] pixels = [];
    private Pixel[][] segments = [];
    private float[] xPixels = [];
    private SKPath? linePath;
    private TransformKey cachedTransform;
    private bool hasCachedTransform;
    private bool rebuildLinePath = true;
    private bool linePathEndsWithFinitePoint;
    private int lastCountTotal = -1;
    private int lastNextIndex = -1;
    private int linePathBaseCountTotal;
    private int linePathCountTotal = -1;

    public DataStreamer Streamer { get; } = streamer;

    public void Render(RenderPack renderPack)
    {
        var currentSegments = GetSegments(renderPack);
        if (currentSegments.Count == 0)
        {
            return;
        }

        UpdateLinePath();
        if (linePath is not null)
        {
            var xOffset = -(Streamer.Data.CountTotal - linePathBaseCountTotal) * GetPixelStep();
            renderPack.Canvas.Save();
            renderPack.Canvas.Translate(xOffset, 0);
            Drawing.DrawLines(renderPack.Canvas, renderPack.Paint, linePath, Streamer.LineStyle);
            renderPack.Canvas.Restore();
        }

        foreach (var segment in currentSegments)
        {
            Drawing.DrawMarkers(renderPack.Canvas, renderPack.Paint, segment, Streamer.MarkerStyle);
        }
    }

    public IReadOnlyList<Pixel[]> GetSegments(RenderPack renderPack)
    {
        var capacity = Streamer.Data.Length;
        var sampleCount = Math.Min(Streamer.Data.CountTotal, capacity);
        if (sampleCount < 2)
        {
            return Array.Empty<Pixel[]>();
        }

        var transform = GetTransformKey(capacity);
        var nextIndex = Streamer.Data.NextIndex;
        var wrapped = sampleCount == capacity;
        var countDelta = Streamer.Data.CountTotal - lastCountTotal;
        var canShiftCachedPixels =
            wrapped &&
            pixels.Length == capacity &&
            hasCachedTransform &&
            cachedTransform == transform &&
            countDelta >= 0 &&
            countDelta < capacity &&
            nextIndex == (lastNextIndex + countDelta) % capacity;

        if (!canShiftCachedPixels)
        {
            RebuildPixels(sampleCount, capacity, nextIndex, wrapped, transform);
            rebuildLinePath = true;
        }
        else if (countDelta > 0)
        {
            ShiftPixels(countDelta, capacity, nextIndex);
        }

        lastCountTotal = Streamer.Data.CountTotal;
        lastNextIndex = nextIndex;
        return segments;
    }

    public void Reset()
    {
        pixels = [];
        segments = [];
        xPixels = [];
        hasCachedTransform = false;
        lastCountTotal = -1;
        lastNextIndex = -1;
        ResetLinePath();
    }

    private void RebuildPixels(
        int sampleCount,
        int capacity,
        int nextIndex,
        bool wrapped,
        TransformKey transform)
    {
        EnsureCapacity(sampleCount);
        for (var screenIndex = 0; screenIndex < sampleCount; screenIndex++)
        {
            var plotIndex = screenIndex + capacity - sampleCount;
            xPixels[screenIndex] = GetPixelX(plotIndex);
            var valueIndex = wrapped
                ? (nextIndex + screenIndex) % capacity
                : screenIndex;
            pixels[screenIndex] = new Pixel(
                xPixels[screenIndex],
                GetPixelY(Streamer.Data.Data[valueIndex]));
        }

        cachedTransform = transform;
        hasCachedTransform = true;
    }

    private void ShiftPixels(int countDelta, int capacity, int nextIndex)
    {
        var retainedCount = capacity - countDelta;
        for (var screenIndex = 0; screenIndex < retainedCount; screenIndex++)
        {
            pixels[screenIndex] = new Pixel(
                xPixels[screenIndex],
                pixels[screenIndex + countDelta].Y);
        }

        for (var screenIndex = retainedCount; screenIndex < capacity; screenIndex++)
        {
            var valueIndex = (nextIndex + screenIndex) % capacity;
            pixels[screenIndex] = new Pixel(
                xPixels[screenIndex],
                GetPixelY(Streamer.Data.Data[valueIndex]));
        }
    }

    private void UpdateLinePath()
    {
        var countDelta = Streamer.Data.CountTotal - linePathCountTotal;
        var pathOffset = Streamer.Data.CountTotal - linePathBaseCountTotal;
        if (rebuildLinePath ||
            linePath is null ||
            countDelta < 0 ||
            countDelta > pixels.Length ||
            pathOffset > Streamer.Data.Length)
        {
            RebuildLinePath();
            return;
        }

        if (countDelta == 0)
        {
            return;
        }

        var xOffset = pathOffset * GetPixelStep();
        for (var index = pixels.Length - countDelta; index < pixels.Length; index++)
        {
            var pixel = pixels[index];
            if (!IsFinite(pixel))
            {
                linePathEndsWithFinitePoint = false;
                continue;
            }

            var point = new SKPoint(pixel.X + xOffset, pixel.Y);
            if (linePathEndsWithFinitePoint)
            {
                linePath.LineTo(point);
            }
            else
            {
                linePath.MoveTo(point);
            }

            linePathEndsWithFinitePoint = true;
        }

        linePathCountTotal = Streamer.Data.CountTotal;
    }

    private void RebuildLinePath()
    {
        linePath?.Dispose();
        linePath = new SKPath();
        linePathEndsWithFinitePoint = false;
        foreach (var pixel in pixels)
        {
            if (!IsFinite(pixel))
            {
                linePathEndsWithFinitePoint = false;
                continue;
            }

            if (linePathEndsWithFinitePoint)
            {
                linePath.LineTo(pixel.ToSKPoint());
            }
            else
            {
                linePath.MoveTo(pixel.ToSKPoint());
            }

            linePathEndsWithFinitePoint = true;
        }

        linePathBaseCountTotal = Streamer.Data.CountTotal;
        linePathCountTotal = Streamer.Data.CountTotal;
        rebuildLinePath = false;
    }

    private void ResetLinePath()
    {
        linePath?.Dispose();
        linePath = null;
        linePathEndsWithFinitePoint = false;
        linePathBaseCountTotal = 0;
        linePathCountTotal = -1;
        rebuildLinePath = true;
    }

    private TransformKey GetTransformKey(int capacity)
    {
        return new TransformKey(
            GetPixelX(0),
            GetPixelX(capacity - 1),
            GetPixelY(0),
            GetPixelY(1));
    }

    private float GetPixelX(int plotIndex)
    {
        var x = plotIndex * Streamer.Data.SamplePeriod + Streamer.Data.OffsetX;
        return Streamer.Axes.GetPixelX(x);
    }

    private float GetPixelY(double value)
    {
        return Streamer.Axes.GetPixelY(value + Streamer.Data.OffsetY);
    }

    private float GetPixelStep()
    {
        return xPixels.Length > 1 ? xPixels[1] - xPixels[0] : 0;
    }

    private void EnsureCapacity(int sampleCount)
    {
        if (pixels.Length == sampleCount)
        {
            return;
        }

        pixels = new Pixel[sampleCount];
        segments = [pixels];
        xPixels = new float[sampleCount];
    }

    private static bool IsFinite(Pixel pixel)
    {
        return float.IsFinite(pixel.X) && float.IsFinite(pixel.Y);
    }

    private readonly record struct TransformKey(float FirstX, float LastX, float ZeroY, float UnitY);
}
