using System;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

public readonly record struct RecordedSessionTrackContentVersion(
    long SessionUpdated,
    Guid? FullTrackId,
    long? FullTrackUpdated);
