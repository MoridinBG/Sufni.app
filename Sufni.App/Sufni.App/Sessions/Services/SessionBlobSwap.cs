using System;

namespace Sufni.App.Sessions.Services;

/// <summary>
/// One queued processed-BLOB swap discovered during the metadata-merge phase of a
/// sync pull: the local row holds a BLOB whose fingerprint differs from the
/// accepted remote (current-schema) fingerprint, so the session-data phase tries
/// to download bytes matching <see cref="TargetFingerprint"/> and swap them in
/// (download-then-swap). The set is transient — never persisted — because an
/// un-advanced last-sync watermark re-derives it from the same metadata delta on
/// the next run.
/// </summary>
public sealed record SessionBlobSwap(Guid SessionId, string TargetFingerprint);
