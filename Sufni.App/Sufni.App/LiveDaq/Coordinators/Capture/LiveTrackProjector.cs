using System.Collections.Generic;
using System.Linq;
using System;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.MapsAndTracks.Models;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Coordinators.Capture;

internal sealed class LiveTrackProjector
{
    private TrackPoint[] sessionTrackPoints = [];
    private TrackPointGeoCoordinate? previousAcceptedGpsCoordinate;

    public TrackPoint[] Points => sessionTrackPoints;

    public void Reset()
    {
        sessionTrackPoints = [];
        previousAcceptedGpsCoordinate = null;
    }

    public void Append(IReadOnlyList<GpsRecord> records, Func<ChunkedBufferSnapshot<GpsRecord>> getCapturedRecords)
    {
        var appendedTrackPoints = new List<TrackPoint>(records.Count);
        var fallbackToFullProjection = false;
        var lastTrackPointTime = sessionTrackPoints.Length == 0
            ? double.NegativeInfinity
            : sessionTrackPoints[^1].Time;
        TrackPoint? previousTrackPoint = sessionTrackPoints.Length == 0 ? null : sessionTrackPoints[^1];
        var previousCoordinate = previousAcceptedGpsCoordinate;

        foreach (var record in records)
        {
            var projected = GpsTrackPointProjection.TryProject(record);
            if (projected is null)
            {
                continue;
            }

            if (projected.Time < lastTrackPointTime)
            {
                fallbackToFullProjection = true;
                break;
            }

            var coordinate = new TrackPointGeoCoordinate(record.Latitude, record.Longitude);
            if (previousTrackPoint is not null && previousCoordinate is { } acceptedCoordinate)
            {
                projected.Speed = TrackPointSeries.CalculateSpeed(
                    previousTrackPoint,
                    projected,
                    acceptedCoordinate,
                    coordinate);
            }

            appendedTrackPoints.Add(projected);
            if (sessionTrackPoints.Length + appendedTrackPoints.Count == 2
                && projected.Speed is { } secondSpeed
                && double.IsFinite(secondSpeed))
            {
                if (sessionTrackPoints.Length == 0)
                {
                    appendedTrackPoints[0].Speed = secondSpeed;
                }
                else
                {
                    var first = sessionTrackPoints[0];
                    sessionTrackPoints = [new TrackPoint(first.Time, first.X, first.Y,
                        first.Elevation, secondSpeed, first.FixMode, first.Satellites, first.Epe2d, first.Epe3d)];
                }
            }

            lastTrackPointTime = projected.Time;
            previousTrackPoint = projected;
            previousCoordinate = coordinate;
        }

        if (fallbackToFullProjection)
        {
            var allRecords = getCapturedRecords().ToArray();
            sessionTrackPoints = [.. GpsTrackPointProjection.ProjectAll(allRecords)];
            previousAcceptedGpsCoordinate = GetLastProjectedCoordinate(allRecords);
        }
        else if (appendedTrackPoints.Count > 0)
        {
            var updatedTrackPoints = new TrackPoint[sessionTrackPoints.Length + appendedTrackPoints.Count];
            Array.Copy(sessionTrackPoints, updatedTrackPoints, sessionTrackPoints.Length);
            appendedTrackPoints.CopyTo(updatedTrackPoints, sessionTrackPoints.Length);
            sessionTrackPoints = updatedTrackPoints;
            previousAcceptedGpsCoordinate = previousCoordinate;
        }

    }

    private static TrackPointGeoCoordinate? GetLastProjectedCoordinate(IEnumerable<GpsRecord> records)
    {
        GpsRecord? lastProjectedRecord = null;
        foreach (var record in records.OrderBy(record => record.Timestamp))
        {
            if (GpsTrackPointProjection.TryProject(record) is not null)
            {
                lastProjectedRecord = record;
            }
        }

        return lastProjectedRecord is null
            ? null
            : new TrackPointGeoCoordinate(lastProjectedRecord.Latitude, lastProjectedRecord.Longitude);
    }
}
