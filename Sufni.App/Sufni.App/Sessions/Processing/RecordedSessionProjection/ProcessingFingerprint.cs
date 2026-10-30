using System;
using System.Text.Json.Serialization;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>
/// Compact description of the inputs that produced processed recorded-session
/// data. It combines the processing algorithm version, dependency identity and
/// hash, raw-source hash, app calibration version, and velocity-filter option.
/// Schema v4 adds the calibration version; absent legacy values read as zero.
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
    RecordedSessionDerivationWindow? DerivationWindow = null,
    int CalibrationVersion = 0)
{
    /// <summary>
    /// True when two fingerprints agree on every DB-resident processing input
    /// (schema/processing/calibration versions, setup, bike, track-projection version,
    /// dependency hash, raw-source hash). This deliberately excludes any
    /// preference-stored processing option: that option does not live in SQLite,
    /// so the in-transaction recompute coherence guard cannot see it and must not
    /// compare it. The option is guarded separately by the recompute engine's
    /// commit-time still-current check.
    /// </summary>
    public bool MatchesDatabaseInputs(ProcessingFingerprint other) =>
        SchemaVersion == other.SchemaVersion &&
        ProcessingVersion == other.ProcessingVersion &&
        CalibrationVersion == other.CalibrationVersion &&
        SetupId == other.SetupId &&
        BikeId == other.BikeId &&
        TrackProjectionVersion == other.TrackProjectionVersion &&
        StringComparer.Ordinal.Equals(DependencyHash, other.DependencyHash) &&
        StringComparer.Ordinal.Equals(SourceHash, other.SourceHash) &&
        DerivationWindow == other.DerivationWindow;
}
