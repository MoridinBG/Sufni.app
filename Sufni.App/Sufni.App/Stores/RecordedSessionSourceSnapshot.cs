using System;
using Sufni.App.Models;

namespace Sufni.App.Stores;

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
