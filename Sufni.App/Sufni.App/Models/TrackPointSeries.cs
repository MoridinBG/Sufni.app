using System;
using System.Collections.Generic;
using System.Linq;

namespace Sufni.App.Models;

internal static class TrackPointSeries
{
    private const double EarthRadiusMeters = 6_371_000;

    public static void CalculateSpeed(
        IList<TrackPoint> points,
        IReadOnlyList<TrackPointGeoCoordinate> sourceCoordinates)
    {
        if (points.Count == 0)
        {
            return;
        }

        points[0].Speed = null;

        for (var i = 1; i < points.Count; i++)
        {
            var previousPoint = points[i - 1];
            var point = points[i];
            point.Speed = CalculateSpeed(previousPoint, point, sourceCoordinates[i - 1], sourceCoordinates[i]);
        }

        if (points.Count > 1 && points[1].Speed is { } firstSpeed && double.IsFinite(firstSpeed))
        {
            points[0].Speed = firstSpeed;
        }
    }

    public static double? CalculateSpeed(
        TrackPoint previousPoint,
        TrackPoint point,
        TrackPointGeoCoordinate previousCoordinate,
        TrackPointGeoCoordinate coordinate)
    {
        var deltaTime = point.Time - previousPoint.Time;

        if (!double.IsFinite(deltaTime) || deltaTime <= 0)
        {
            return null;
        }

        var distance = CalculateHaversineDistance(previousCoordinate, coordinate);
        var speed = distance / deltaTime;
        return double.IsFinite(speed) ? speed : null;
    }

    public static bool HasSpeedSeries(IReadOnlyList<TrackPoint>? points)
    {
        return points?.Count(point => double.IsFinite(point.Time)
                                      && point.Speed is { } speed
                                      && double.IsFinite(speed)) >= 2;
    }

    public static bool HasElevationSeries(IReadOnlyList<TrackPoint>? points)
    {
        return points?.Count(point => double.IsFinite(point.Time)
                                      && point.Elevation is { } elevation
                                      && double.IsFinite(elevation)) >= 2;
    }

    public static TrackTimelineContext? BuildTimelineContext(
        IReadOnlyList<TrackPoint>? points,
        double? originSeconds,
        double? durationSeconds)
    {
        if (originSeconds is { } origin
            && durationSeconds is { } duration
            && double.IsFinite(origin)
            && double.IsFinite(duration)
            && duration > 0)
        {
            return new TrackTimelineContext(origin, duration);
        }

        if (points is null || points.Count < 2)
        {
            return null;
        }

        var first = points.FirstOrDefault(point => double.IsFinite(point.Time));
        var last = points.LastOrDefault(point => double.IsFinite(point.Time));
        if (first is null || last is null)
        {
            return null;
        }

        var derivedDuration = last.Time - first.Time;
        return double.IsFinite(derivedDuration) && derivedDuration > 0
            ? new TrackTimelineContext(first.Time, derivedDuration)
            : null;
    }

    private static double CalculateHaversineDistance(TrackPointGeoCoordinate first, TrackPointGeoCoordinate second)
    {
        var firstLatitude = ToRadians(first.Latitude);
        var secondLatitude = ToRadians(second.Latitude);
        var deltaLatitude = ToRadians(second.Latitude - first.Latitude);
        var deltaLongitude = ToRadians(second.Longitude - first.Longitude);

        var sinHalfLatitude = Math.Sin(deltaLatitude / 2);
        var sinHalfLongitude = Math.Sin(deltaLongitude / 2);
        var a = sinHalfLatitude * sinHalfLatitude
                + Math.Cos(firstLatitude) * Math.Cos(secondLatitude) * sinHalfLongitude * sinHalfLongitude;
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusMeters * c;
    }

    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180;
    }
}
