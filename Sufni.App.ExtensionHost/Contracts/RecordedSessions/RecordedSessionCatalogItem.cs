using System;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

public sealed record RecordedSessionCatalogItem(
    Guid Id,
    string Name,
    long? Timestamp,
    double? DurationSeconds = null)
{
    public RecordedSessionTrackContentVersion? TrackContentVersion { get; init; }
}
