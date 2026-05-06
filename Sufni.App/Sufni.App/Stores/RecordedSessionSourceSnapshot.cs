using System;
using Sufni.App.Models;

namespace Sufni.App.Stores;

/// <summary>
/// Lightweight metadata snapshot for a recorded raw source.
/// It intentionally excludes the source payload so list and domain state can
/// track source presence and hashes without holding raw bytes in memory.
/// </summary>
public sealed record RecordedSessionSourceSnapshot(
    Guid SessionId,
    RecordedSessionSourceKind SourceKind,
    string SourceName,
    int SchemaVersion,
    string SourceHash)
{
    public static RecordedSessionSourceSnapshot From(RecordedSessionSource source) => new(
        source.SessionId,
        source.SourceKind,
        source.SourceName,
        source.SchemaVersion,
        source.SourceHash);
}
