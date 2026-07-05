using System;

namespace Sufni.App.Sessions.Models;

// Network transfer shape for a recorded-session source. The payload bytes move
// as an octet-stream body; row metadata travels in HTTP headers.
public sealed record RecordedSessionSourcePayload(
    Guid SessionId,
    RecordedSessionSourceKind SourceKind,
    string SourceName,
    int SchemaVersion,
    string SourceHash,
    byte[] Payload);
