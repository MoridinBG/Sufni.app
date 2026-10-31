using System;

namespace Sufni.App.Sessions.Models;

public sealed record SessionProcessedGeneration(
    double? DurationSeconds)
{
    public static SessionProcessedGeneration From(Session session) => new(
        session.DurationSeconds);
}
