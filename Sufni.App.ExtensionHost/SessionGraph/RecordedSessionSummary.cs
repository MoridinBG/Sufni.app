using System;

namespace Sufni.App.SessionGraph;

/// <summary>
/// Lightweight recorded-session row state.
/// It carries the fields needed to present and sort a session list together
/// with the current processed-data staleness classification and update
/// baseline for list-level recomputation.
/// </summary>
public sealed record RecordedSessionSummary(
    Guid Id,
    long Updated,
    string Name,
    string Description,
    long? Timestamp,
    bool HasProcessedData,
    SessionStaleness Staleness,
    double? DurationSeconds = null,
    double? DistanceMeters = null,
    double? AscentMeters = null,
    double? DescentMeters = null);
