using System;

namespace Sufni.App.SessionGraph;

public sealed record RecordedSessionSummary(
    Guid Id,
    string Name,
    string Description,
    long? Timestamp,
    bool HasProcessedData,
    SessionStaleness Staleness);
