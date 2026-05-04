using System;
using System.Text.Json.Serialization;

namespace Sufni.App.Models;

public sealed record RecordedSessionSourceTransfer(
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("source_kind")] RecordedSessionSourceKind SourceKind,
    [property: JsonPropertyName("source_name")] string SourceName,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("source_hash")] string SourceHash,
    [property: JsonPropertyName("payload")] byte[] Payload);
