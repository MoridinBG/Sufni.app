using System;

namespace Sufni.App.Sessions.Models;

public sealed record SessionProcessedGeneration(
    double? DurationSeconds,
    Guid? FullTrackId)
{
    public static SessionProcessedGeneration From(Session session) => new(
        session.DurationSeconds,
        session.FullTrack);
}
