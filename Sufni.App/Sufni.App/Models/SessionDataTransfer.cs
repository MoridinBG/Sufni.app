using System.Text.Json.Serialization;

namespace Sufni.App.Models;

// Network transfer shape for a processed-telemetry blob. It carries the
// fingerprint of the bytes alongside them so the receiver can verify coherence
// before committing (download-then-swap, sync coherence): a download
// commits only when this fingerprint matches the receiver's target, and an
// upload is rejected when it does not match the hub's stored fingerprint. The
// fingerprint is the session's stored session_processing_fingerprint JSON;
// null/empty means a legacy or never-fingerprinted blob.
public sealed record SessionDataTransfer(
    [property: JsonPropertyName("processing_fingerprint")] string? Fingerprint,
    [property: JsonPropertyName("data")] byte[] Data);
