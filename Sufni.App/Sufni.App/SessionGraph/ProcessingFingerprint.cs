using System;

namespace Sufni.App.SessionGraph;

/// <summary>
/// Compact description of the inputs that produced processed recorded-session
/// data. It combines the processing algorithm version, dependency identity and
/// hash, and raw-source hash into a value suitable for stale-cache checks.
/// </summary>
public sealed record ProcessingFingerprint(
    int SchemaVersion,
    int ProcessingVersion,
    Guid SetupId,
    Guid BikeId,
    string DependencyHash,
    string SourceHash);
