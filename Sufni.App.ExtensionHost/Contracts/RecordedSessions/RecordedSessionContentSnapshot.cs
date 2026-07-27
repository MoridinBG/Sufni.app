using System;
using System.Collections.Generic;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.Telemetry;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

public readonly record struct RecordedSessionContentToken(
    long ProcessedTelemetryRevision,
    long TrackProjectionRevision,
    Guid? FullTrackId,
    long? FullTrackPointsRevision,
    long? Timestamp,
    double? DurationSeconds,
    double GpsOffsetSeconds);

[Flags]
public enum RecordedSessionContentSelection
{
    None = 0,
    ProcessedTelemetry = 1 << 0,
    Track = 1 << 1,
    All = ProcessedTelemetry | Track,
}

public sealed record RecordedSessionContentSnapshot(
    RecordedSessionContentToken Token,
    RecordedSessionContentSelection Selection,
    TelemetryData? ProcessedTelemetry,
    IReadOnlyList<TrackPoint>? Track);

public abstract record RecordedSessionContentSnapshotResult
{
    private RecordedSessionContentSnapshotResult()
    {
    }

    public sealed record Available(RecordedSessionContentSnapshot Snapshot)
        : RecordedSessionContentSnapshotResult;

    public sealed record Stale : RecordedSessionContentSnapshotResult;

    public sealed record Missing : RecordedSessionContentSnapshotResult;
}
