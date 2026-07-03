using System;
using System.Text.Json.Serialization;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>
/// Compact description of the inputs that produced processed recorded-session
/// data. It combines the processing algorithm version, dependency identity and
/// hash, raw-source hash, and the clamped velocity-filter processing option into
/// a value suitable for stale-cache checks. Schema v3 added
/// <see cref="VelocityFilterWindowMilliseconds"/>; the field defaults to the
/// 25 ms default so legacy/test constructions stay valid. Derivation windows are
/// null-omitted so existing v3 fingerprint JSON remains byte-stable.
/// </summary>
public sealed record ProcessingFingerprint(
    int SchemaVersion,
    int ProcessingVersion,
    Guid SetupId,
    Guid BikeId,
    int TrackProjectionVersion,
    string DependencyHash,
    string SourceHash,
    int VelocityFilterWindowMilliseconds = TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    RecordedSessionDerivationWindow? DerivationWindow = null)
{
    /// <summary>
    /// True when two fingerprints agree on every DB-resident processing input
    /// (schema/processing version, setup, bike, track-projection version,
    /// dependency hash, raw-source hash). This deliberately excludes any
    /// preference-stored processing option: that option does not live in SQLite,
    /// so the in-transaction recompute coherence guard cannot see it and must not
    /// compare it. The option is guarded separately by the recompute engine's
    /// commit-time still-current check.
    /// </summary>
    public bool MatchesDatabaseInputs(ProcessingFingerprint other) =>
        SchemaVersion == other.SchemaVersion &&
        ProcessingVersion == other.ProcessingVersion &&
        SetupId == other.SetupId &&
        BikeId == other.BikeId &&
        TrackProjectionVersion == other.TrackProjectionVersion &&
        StringComparer.Ordinal.Equals(DependencyHash, other.DependencyHash) &&
        StringComparer.Ordinal.Equals(SourceHash, other.SourceHash);
}
