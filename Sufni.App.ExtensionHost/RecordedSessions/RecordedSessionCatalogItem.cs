using System;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public sealed record RecordedSessionCatalogItem(
    Guid Id,
    string Name,
    long? Timestamp,
    double? DurationSeconds = null);
