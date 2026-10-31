using System;
using System.Collections.Generic;
using System.Linq;
using Mapsui.Projections;

namespace Sufni.App.MapsAndTracks.Models;

public sealed record GpsMetrics(
    double? DistanceMeters,
    double? AscentMeters,
    double? DescentMeters);

internal static class GpsMetricsCalculator
{
    public const int AlgorithmVersion = 1;
    private const double ElevationNoiseThresholdMeters = 3.0;

    public static GpsMetrics Calculate(IReadOnlyList<TrackPoint>? points)
    {
        var distance = CalculateDistance(points);
        var (ascent, descent) = CalculateElevationGain(points);

        return new GpsMetrics(distance, ascent, descent);
    }

    private static double? CalculateDistance(IReadOnlyList<TrackPoint>? points)
    {
        var finitePoints = FilterFinitePositionPoints(points);
        if (finitePoints is null || finitePoints.Count < 2)
        {
            return null;
        }

        var distance = 0.0;
        for (var i = 1; i < finitePoints.Count; i++)
        {
            var previous = finitePoints[i - 1];
            var current = finitePoints[i];
            var segment = TrackPointSeries.CalculateHaversineDistance(
                ToGeoCoordinate(previous),
                ToGeoCoordinate(current));
            if (double.IsFinite(segment))
            {
                distance += segment;
            }
        }

        return double.IsFinite(distance) ? distance : null;
    }

    private static (double? AscentMeters, double? DescentMeters) CalculateElevationGain(IReadOnlyList<TrackPoint>? points)
    {
        var elevations = FilterFinitePositionPoints(points)?
            .Select(point => point.Elevation)
            .Where(elevation => elevation is { } value && double.IsFinite(value))
            .Select(elevation => elevation!.Value)
            .ToList();
        if (elevations is null || elevations.Count < 2)
        {
            return (null, null);
        }

        var ascent = 0.0;
        var descent = 0.0;
        var referenceElevation = elevations[0];
        for (var i = 1; i < elevations.Count; i++)
        {
            var currentElevation = elevations[i];
            var delta = currentElevation - referenceElevation;
            if (delta >= ElevationNoiseThresholdMeters)
            {
                ascent += delta;
                referenceElevation = currentElevation;
            }
            else if (delta <= -ElevationNoiseThresholdMeters)
            {
                descent -= delta;
                referenceElevation = currentElevation;
            }
        }

        return (ascent, descent);
    }

    private static List<TrackPoint>? FilterFinitePositionPoints(IReadOnlyList<TrackPoint>? points) =>
        points?
            .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y))
            .ToList();

    private static TrackPointGeoCoordinate ToGeoCoordinate(TrackPoint point)
    {
        var (longitude, latitude) = SphericalMercator.ToLonLat(point.X, point.Y);
        return new TrackPointGeoCoordinate(latitude, longitude);
    }
}
