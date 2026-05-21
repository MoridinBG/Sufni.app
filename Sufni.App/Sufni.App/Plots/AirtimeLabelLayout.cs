using System;
using System.Collections.Generic;
using System.Linq;

namespace Sufni.App.Plots;

internal readonly record struct AirtimeLabelLayoutCandidate(
    int Index,
    double CenterSeconds,
    double WidthPixels,
    double Priority);

internal static class AirtimeLabelLayout
{
    public const double DefaultLabelGapPixels = 8.0;

    public static bool[] SelectVisibleLabels(
        IReadOnlyList<AirtimeLabelLayoutCandidate> candidates,
        double visibleMinimumSeconds,
        double visibleMaximumSeconds,
        double dataAreaWidthPixels,
        double labelGapPixels = DefaultLabelGapPixels)
    {
        var visibleLabels = new bool[candidates.Count];
        if (candidates.Count == 0 ||
            dataAreaWidthPixels <= 0 ||
            double.IsNaN(dataAreaWidthPixels) ||
            double.IsInfinity(dataAreaWidthPixels))
        {
            return visibleLabels;
        }

        var visibleLeft = Math.Min(visibleMinimumSeconds, visibleMaximumSeconds);
        var visibleRight = Math.Max(visibleMinimumSeconds, visibleMaximumSeconds);
        var visibleSpan = visibleRight - visibleLeft;
        if (visibleSpan <= 0)
        {
            return visibleLabels;
        }

        var acceptedBounds = new List<PixelBounds>();
        foreach (var candidate in candidates
                     .OrderByDescending(candidate => candidate.Priority)
                     .ThenBy(candidate => candidate.CenterSeconds)
                     .ThenBy(candidate => candidate.Index))
        {
            if (candidate.CenterSeconds < visibleLeft || candidate.CenterSeconds > visibleRight)
            {
                continue;
            }

            var centerPixels = (candidate.CenterSeconds - visibleLeft) / visibleSpan * dataAreaWidthPixels;
            var halfWidth = Math.Max(0, candidate.WidthPixels) / 2.0;
            var bounds = new PixelBounds(
                centerPixels - halfWidth - labelGapPixels / 2.0,
                centerPixels + halfWidth + labelGapPixels / 2.0);

            if (acceptedBounds.Any(bounds.Overlaps))
            {
                continue;
            }

            acceptedBounds.Add(bounds);
            if ((uint)candidate.Index < visibleLabels.Length)
            {
                visibleLabels[candidate.Index] = true;
            }
        }

        return visibleLabels;
    }

    private readonly record struct PixelBounds(double Left, double Right)
    {
        public bool Overlaps(PixelBounds other) => Left < other.Right && Right > other.Left;
    }
}
