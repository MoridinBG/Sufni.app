using System;

namespace Sufni.App.SessionGraph;

/// <summary>
/// Flags describing why a recorded-session domain snapshot changed.
/// Multiple causes can be present when related metadata, source, dependency,
/// or fingerprint changes are coalesced into one emission.
/// </summary>
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
