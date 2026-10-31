using System;

namespace Sufni.App.MapsAndTracks.Models;

/// <summary>Inputs of one exact session-window projection.</summary>
public readonly record struct SessionTrackKey(
    Guid TrackId,
    long PointsRevision,
    long Timestamp,
    double DurationSeconds,
    double GpsOffsetSeconds,
    int ProjectionVersion = GpsTrackPointProjection.ProjectionVersion,
    double WindowOriginFraction = 0);
