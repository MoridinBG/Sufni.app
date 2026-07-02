using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.App.ExtensionHost.Contracts.Models;

namespace Sufni.App.MapsAndTracks.Models;

internal sealed class TrackPointTimeIndex
{
    private readonly double[] times;
    private readonly TrackPoint[] points;

    public TrackPointTimeIndex(IReadOnlyList<TrackPoint> points)
    {
        var finitePoints = points
            .Select((point, index) => (Point: point, Index: index))
            .Where(item => double.IsFinite(item.Point.Time))
            .OrderBy(item => item.Point.Time)
            .ThenBy(item => item.Index)
            .ToArray();

        this.points = new TrackPoint[finitePoints.Length];
        times = new double[finitePoints.Length];

        for (var index = 0; index < finitePoints.Length; index++)
        {
            this.points[index] = finitePoints[index].Point;
            times[index] = finitePoints[index].Point.Time;
        }

        TimelineContext = BuildTimelineContext(times);
    }

    public TrackTimeRange? TimelineContext { get; }

    public TrackPoint? FindClosest(double targetTime)
    {
        if (points.Length == 0 || !double.IsFinite(targetTime))
        {
            return null;
        }

        var candidateIndex = LowerBound(targetTime);
        if (candidateIndex == 0)
        {
            return points[0];
        }

        if (candidateIndex == points.Length)
        {
            return points[^1];
        }

        var previousIndex = candidateIndex - 1;
        var previousDistance = targetTime - times[previousIndex];
        var candidateDistance = times[candidateIndex] - targetTime;
        return previousDistance <= candidateDistance
            ? points[previousIndex]
            : points[candidateIndex];
    }

    public IReadOnlyList<TrackPoint> GetRangeWithBoundaryNeighbors(double startSeconds, double endSeconds)
    {
        if (points.Length == 0 ||
            !double.IsFinite(startSeconds) ||
            !double.IsFinite(endSeconds) ||
            startSeconds > endSeconds)
        {
            return [];
        }

        var firstInRange = LowerBound(startSeconds);
        var firstAfterRange = UpperBound(endSeconds);
        var startIndex = firstInRange > 0 ? firstInRange - 1 : firstInRange;
        var endExclusive = firstAfterRange < points.Length ? firstAfterRange + 1 : firstAfterRange;
        if (startIndex >= endExclusive)
        {
            return [];
        }

        return points[startIndex..endExclusive];
    }

    private int LowerBound(double value)
    {
        var low = 0;
        var high = times.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (times[middle] < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private int UpperBound(double value)
    {
        var low = 0;
        var high = times.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (times[middle] <= value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static TrackTimeRange? BuildTimelineContext(IReadOnlyList<double> sortedTimes)
    {
        if (sortedTimes.Count < 2)
        {
            return null;
        }

        var duration = sortedTimes[^1] - sortedTimes[0];
        return duration > 0 && double.IsFinite(duration)
            ? new TrackTimeRange(sortedTimes[0], duration)
            : null;
    }
}
