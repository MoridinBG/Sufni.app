using System;

namespace Sufni.App.SessionGraph;

[Flags]
public enum DerivedChangeKind
{
    None = 0,
    Initial = 1 << 0,
    SessionMetadataChanged = 1 << 1,
    ProcessedDataAvailabilityChanged = 1 << 2,
    SourceAvailabilityChanged = 1 << 3,
    DependencyChanged = 1 << 4,
    FingerprintChanged = 1 << 5
}
