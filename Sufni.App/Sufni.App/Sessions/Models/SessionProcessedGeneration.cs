using System;
using System.Collections.Generic;
using Sufni.App.MapsAndTracks.Models;

namespace Sufni.App.Sessions.Models;

public sealed record SessionProcessedGeneration(
    double? DurationSeconds,
    double? DistanceMeters,
    double? AscentMeters,
    double? DescentMeters,
    Guid? FullTrackId,
    List<TrackPoint>? Track)
{
    public static SessionProcessedGeneration From(Session session) => new(
        session.DurationSeconds,
        session.DistanceMeters,
        session.AscentMeters,
        session.DescentMeters,
        session.FullTrack,
        session.Track is null ? null : [.. session.Track]);
}
