using System;

using Sufni.App.Segments.Models;
namespace Sufni.App.Segments.Stores;

public sealed record SessionSegmentSnapshot(
    Guid Id,
    Guid SessionId,
    string Name,
    double StartSeconds,
    double EndSeconds)
{
    public static SessionSegmentSnapshot From(SessionSegment segment) => new(
        segment.Id,
        segment.SessionId,
        segment.Name,
        segment.StartSeconds,
        segment.EndSeconds);
}
