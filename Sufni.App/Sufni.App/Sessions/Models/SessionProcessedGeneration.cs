using System;

namespace Sufni.App.Sessions.Models;

public sealed record SessionProcessedGeneration(
    double? DurationSeconds,
    double? DistanceMeters,
    double? AscentMeters,
    double? DescentMeters,
    Guid? FullTrackId)
{
    public static SessionProcessedGeneration From(Session session) => new(
        session.DurationSeconds,
        session.DistanceMeters,
        session.AscentMeters,
        session.DescentMeters,
        session.FullTrack);
}
