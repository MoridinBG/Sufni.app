using System;

namespace Sufni.App.SessionGraph;

/// <summary>
/// Lightweight recorded-session row state.
/// It carries the fields needed to present and sort a session list together
/// with the current processed-data staleness classification.
/// </summary>
public sealed record RecordedSessionSummary(
    Guid Id,
    string Name,
    string Description,
    long? Timestamp,
    bool HasProcessedData,
    SessionStaleness Staleness);
