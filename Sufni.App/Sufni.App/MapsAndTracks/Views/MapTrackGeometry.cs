using System;
using System.Collections.Generic;
using Mapsui.Projections;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.MapsAndTracks.Views;

internal static class MapTrackGeometry
{
    internal static (double X, double Y) ProjectMapCoordinate(RecordedSessionMapCoordinate coordinate)
    {
        var (x, y) = SphericalMercator.FromLonLat(coordinate.Longitude, coordinate.Latitude);
        return (x, y);
    }

    internal static bool TryGetVisibleTrackRange(
        IReadOnlyList<TrackPoint> sessionTrackPoints,
        TrackTimeRange context,
        double minX,
        double maxX,
        double minY,
        double maxY,
        out double start,
        out double end)
    {
        var firstVisible = -1;
        var lastVisible = -1;

        for (var i = 0; i < sessionTrackPoints.Count; i++)
        {
            var point = sessionTrackPoints[i];
            if (IsPointVisible(point, minX, maxX, minY, maxY))
            {
                if (firstVisible == -1) firstVisible = i;
                lastVisible = i;
            }

            if (i == 0 || !SegmentIntersectsViewport(sessionTrackPoints[i - 1], point, minX, maxX, minY, maxY))
            {
                continue;
            }

            if (firstVisible == -1) firstVisible = i - 1;
            lastVisible = i;
        }

        if (firstVisible < 0 || lastVisible <= firstVisible)
        {
            start = 0;
            end = 1;
            return false;
        }

        var firstTime = sessionTrackPoints[firstVisible].Time;
        var lastTime = sessionTrackPoints[lastVisible].Time;
        if (!double.IsFinite(firstTime) || !double.IsFinite(lastTime))
        {
            start = 0;
            end = 1;
            return false;
        }

        start = NormalizeTime(firstTime, context);
        end = NormalizeTime(lastTime, context);
        return true;
    }

    internal static TrackPoint? FindClosestTrackPoint(IReadOnlyList<TrackPoint> sessionTrackPoints, double targetTime)
    {
        TrackPoint? closest = null;
        var closestDistance = double.PositiveInfinity;

        foreach (var point in sessionTrackPoints)
        {
            if (!double.IsFinite(point.Time))
            {
                continue;
            }

            var distance = Math.Abs(point.Time - targetTime);
            if (distance >= closestDistance)
            {
                continue;
            }

            closest = point;
            closestDistance = distance;
        }

        return closest;
    }

    internal static List<TrackPoint> GetTrackPointsInTimeRange(
        IReadOnlyList<TrackPoint> sessionTrackPoints,
        double startSeconds,
        double endSeconds)
    {
        var pointsInRange = new List<TrackPoint>();
        TrackPoint? before = null;
        TrackPoint? after = null;

        foreach (var point in sessionTrackPoints)
        {
            if (!double.IsFinite(point.Time))
            {
                continue;
            }

            if (point.Time < startSeconds)
            {
                before = point;
                continue;
            }

            if (point.Time > endSeconds)
            {
                after ??= point;
                continue;
            }

            pointsInRange.Add(point);
        }

        if (before is not null)
        {
            pointsInRange.Insert(0, before);
        }

        if (after is not null)
        {
            pointsInRange.Add(after);
        }

        return pointsInRange;
    }

    internal static double NormalizeTime(double timeSeconds, TrackTimeRange context)
    {
        return Math.Clamp((timeSeconds - context.OriginSeconds) / context.DurationSeconds, 0, 1);
    }

    internal static bool SegmentIntersectsViewport(
        TrackPoint start,
        TrackPoint end,
        double minX,
        double maxX,
        double minY,
        double maxY)
    {
        var x0 = start.X;
        var y0 = start.Y;
        var dx = end.X - x0;
        var dy = end.Y - y0;
        var entering = 0d;
        var leaving = 1d;

        return ClipSegment(-dx, x0 - minX, ref entering, ref leaving)
            && ClipSegment(dx, maxX - x0, ref entering, ref leaving)
            && ClipSegment(-dy, y0 - minY, ref entering, ref leaving)
            && ClipSegment(dy, maxY - y0, ref entering, ref leaving);
    }

    internal static bool ClipSegment(double direction, double distance, ref double entering, ref double leaving)
    {
        if (Math.Abs(direction) <= 0.000000001)
        {
            return distance >= 0;
        }

        var ratio = distance / direction;
        if (direction < 0)
        {
            if (ratio > leaving) return false;
            if (ratio > entering) entering = ratio;
        }
        else
        {
            if (ratio < entering) return false;
            if (ratio < leaving) leaving = ratio;
        }

        return true;
    }

    private static bool IsPointVisible(TrackPoint point, double minX, double maxX, double minY, double maxY)
    {
        return point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY;
    }
}
