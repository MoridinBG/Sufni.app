using System;
using System.Text.Json.Serialization;

namespace Sufni.App.Models;

/// <summary>
/// Network transfer shape for a recorded-session source.
/// It carries the source metadata and payload in the same form needed to
/// reconstruct the persisted raw-source row on another device.
/// </summary>
public sealed record RecordedSessionSourceTransfer(
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("source_kind")] RecordedSessionSourceKind SourceKind,
    [property: JsonPropertyName("source_name")] string SourceName,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("source_hash")] string SourceHash,
    [property: JsonPropertyName("payload")] byte[] Payload);
