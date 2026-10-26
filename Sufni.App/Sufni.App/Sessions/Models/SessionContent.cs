using System;
using System.Collections.Generic;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Models;

/// <summary>
/// Identifies the processed telemetry and track content of a session. Two equal
/// tokens read the same content.
/// </summary>
public readonly record struct SessionContentToken(
    long ProcessedTelemetryRevision,
    long TrackProjectionRevision,
    Guid? FullTrackId,
    long? FullTrackPointsRevision,
    long? Timestamp,
    double? DurationSeconds,
    double GpsOffsetSeconds);

public sealed record SessionCatalogItem(
    Guid Id,
    string Name,
    long? Timestamp,
    double? DurationSeconds,
    SessionContentToken ContentToken);

[Flags]
public enum SessionContentSelection
{
    None = 0,
    ProcessedTelemetry = 1 << 0,
    Track = 1 << 1,
    All = ProcessedTelemetry | Track,
}

public sealed record SessionContentSnapshot(
    SessionContentToken Token,
    SessionContentSelection Selection,
    TelemetryData? ProcessedTelemetry,
    IReadOnlyList<TrackPoint>? Track);

public abstract record SessionContentSnapshotResult
{
    private SessionContentSnapshotResult()
    {
    }

    public sealed record Available(SessionContentSnapshot Snapshot) : SessionContentSnapshotResult;

    /// <summary>The session content changed after the token was read.</summary>
    public sealed record Stale : SessionContentSnapshotResult;

    public sealed record Missing : SessionContentSnapshotResult;
}
