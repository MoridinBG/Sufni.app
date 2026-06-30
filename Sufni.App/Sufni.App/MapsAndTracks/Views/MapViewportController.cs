using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.App.ExtensionHost.Contracts.Models;

namespace Sufni.App.MapsAndTracks.Views;

/// <summary>
/// Pure viewport math for <see cref="MapView"/>: fit-to-track extents with
/// degenerate-window expansion, and viewport-to-world bounds. No Mapsui
/// dependency so the math is directly unit-testable.
/// </summary>
internal static class MapViewportController
{
    public readonly record struct ViewportBounds(double MinX, double MaxX, double MinY, double MaxY);

    public abstract record RangeFit;

    /// <summary>Point-like window: center on it, clamping the resolution.</summary>
    public sealed record CenterFit(double X, double Y, double MaxResolution) : RangeFit;

    /// <summary>Regular window: zoom to the padded extent.</summary>
    public sealed record ExtentFit(double MinX, double MinY, double MaxX, double MaxY) : RangeFit;

    public static RangeFit? ComputeRangeFit(IReadOnlyList<TrackPoint> pointsInRange, double padding)
    {
        if (pointsInRange.Count == 0)
        {
            return null;
        }

        var minX = pointsInRange.Min(p => p.X);
        var maxX = pointsInRange.Max(p => p.X);
        var minY = pointsInRange.Min(p => p.Y);
        var maxY = pointsInRange.Max(p => p.Y);

        var width = maxX - minX;
        var height = maxY - minY;
        if (width <= 0 && height <= 0)
        {
            return new CenterFit((minX + maxX) / 2, (minY + maxY) / 2, MaxResolution: 10);
        }

        // Straight-line windows get expanded to a non-zero extent so the
        // viewport fit still zooms visibly.
        if (width <= 0)
        {
            var halfWidth = height / 2.0;
            minX -= halfWidth;
            maxX += halfWidth;
            width = maxX - minX;
        }

        if (height <= 0)
        {
            var halfHeight = width / 2.0;
            minY -= halfHeight;
            maxY += halfHeight;
            height = maxY - minY;
        }

        var paddingX = width * padding;
        var paddingY = height * padding;

        return new ExtentFit(
            minX - paddingX,
            minY - paddingY,
            maxX + paddingX,
            maxY + paddingY);
    }

    public static ViewportBounds ComputeBounds(
        double centerX,
        double centerY,
        double width,
        double height,
        double resolution)
    {
        var halfWidth = width * resolution / 2;
        var halfHeight = height * resolution / 2;
        return new ViewportBounds(
            centerX - halfWidth,
            centerX + halfWidth,
            centerY - halfHeight,
            centerY + halfHeight);
    }
}
