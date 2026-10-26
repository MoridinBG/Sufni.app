using System;
using System.Collections.Generic;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.Telemetry;

using Sufni.App.Sessions.Models;

namespace Sufni.App.Segments.Models;

public sealed record SegmentMatchSourceContext(
    Guid SessionId,
    TelemetryTimeRange Range,
    SessionContentToken ContentToken);

public sealed record SegmentMatchRequest(
    IReadOnlyList<TrackPoint> SegmentPoints,
    SegmentMatchSourceContext? Source,
    SegmentDtwMatchOptions Options);

public sealed record SegmentMatch(
    Guid SessionId,
    string SessionName,
    long? SessionTimestamp,
    double StartSeconds,
    double EndSeconds,
    double NormalizedCostMeters,
    double MedianErrorMeters,
    double P90ErrorMeters,
    int SourcePointCount,
    int CandidatePointCount,
    IReadOnlyList<SegmentTimeMapPoint> TimeMap,
    SessionContentToken ContentToken);

public readonly record struct SegmentTimeMapPoint(
    double SourceSeconds,
    double MatchSeconds);

public sealed record SegmentDtwMatchOptions(
    double ResampleStepMeters,
    double MinSegmentLengthMeters,
    double MaxPointDistanceMeters,
    double MaxNormalizedCostMeters,
    double CandidateLengthToleranceRatio,
    int MaxDegreeOfParallelism)
{
    public static SegmentDtwMatchOptions Default { get; } = new(
        ResampleStepMeters: 5,
        MinSegmentLengthMeters: 150,
        MaxPointDistanceMeters: 35,
        MaxNormalizedCostMeters: 20,
        CandidateLengthToleranceRatio: 0.35,
        MaxDegreeOfParallelism: Environment.ProcessorCount);
}

public abstract record SegmentMatchProgress
{
    public sealed record LoadingSessions : SegmentMatchProgress;

    public sealed record EnumeratingSessions(int CurrentSession, int TotalSessions)
        : SegmentMatchProgress;

    /// <param name="CurrentSessionFraction">
    /// Progress (0..1) through the session currently being matched, so long sessions
    /// can drive the indicator between completion ticks.
    /// </param>
    public sealed record MatchingSessions(
        int CompletedSessions,
        int TotalSessions,
        double CurrentSessionFraction = 0)
        : SegmentMatchProgress;
}
