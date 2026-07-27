using System;
using Sufni.App.Sessions.Models;

namespace Sufni.App.Sessions.Services;

/// <summary>
/// One queued processed-BLOB swap discovered during the metadata-merge phase of a
/// sync pull: the local row holds a BLOB whose fingerprint differs from the
/// accepted remote (current-schema) fingerprint, so the session-data phase tries
/// to download bytes matching <see cref="TargetFingerprint"/> and swap them in
/// (download-then-swap). The optional target generation travels with newer requests;
/// legacy requests can still complete through the existing fingerprint-only path.
/// The set is transient — never persisted — because an un-advanced pull cursor
/// re-derives it from the same metadata delta on the next run.
/// </summary>
public sealed record SessionBlobSwap(
    Guid SessionId,
    string TargetFingerprint,
    SessionProcessedGeneration? TargetGeneration = null);
