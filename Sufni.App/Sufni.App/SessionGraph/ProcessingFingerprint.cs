using System;

namespace Sufni.App.SessionGraph;

public sealed record ProcessingFingerprint(
    int SchemaVersion,
    int ProcessingVersion,
    Guid SetupId,
    Guid BikeId,
    string DependencyHash,
    string SourceHash);
