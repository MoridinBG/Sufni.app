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
    public bool MatchesDatabaseInputs(ProcessingFingerprint other) => this == other;
}
