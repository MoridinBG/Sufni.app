using System;

namespace Sufni.App.Sessions.Processing.SessionGraph;

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

    // User-authored metadata only (Name, Description, SetupId, Timestamp, tuning).
    // The session-window track fields are tracked separately by DerivedTrackChanged.
    SessionMetadataChanged = 1 << 1,
    ProcessedDataAvailabilityChanged = 1 << 2,
    SourceAvailabilityChanged = 1 << 3,
    DependencyChanged = 1 << 4,
    FingerprintChanged = 1 << 5,

    // The derived session-window track changed (full-track association or GPS
    // offset). Not user-authored: the editor refreshes the track display without
    // a discard-edits prompt, and it never triggers a recompute prompt.
    DerivedTrackChanged = 1 << 6
}
