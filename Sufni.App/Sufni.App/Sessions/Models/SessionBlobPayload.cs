namespace Sufni.App.Sessions.Models;

// Network transfer shape for a processed-telemetry blob. The bytes move as an
// octet-stream body; the fingerprint travels as HTTP metadata so the receiver
// can verify coherence before committing (download-then-swap, sync coherence).
public sealed record SessionBlobPayload(string? Fingerprint, byte[] Data);
