using System;
using System.Collections.Generic;
using Sufni.App.ExtensionHost.Contracts.Models;

namespace Sufni.App.Sessions.Models;

public sealed record SessionProcessedGeneration(
    double? DurationSeconds,
    double? DistanceMeters,
    double? AscentMeters,
    double? DescentMeters,
    Guid? FullTrackId,
    double GpsOffsetSeconds,
    List<TrackPoint>? Track)
{
    public static SessionProcessedGeneration From(Session session) => new(
        session.DurationSeconds,
        session.DistanceMeters,
        session.AscentMeters,
        session.DescentMeters,
        session.FullTrack,
        session.GpsOffsetSeconds,
        session.Track is null ? null : [.. session.Track]);
}
