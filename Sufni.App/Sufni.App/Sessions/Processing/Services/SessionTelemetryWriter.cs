using System;
using System.IO;
using System.Threading.Tasks;

using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
namespace Sufni.App.Sessions.Processing.Services;

/// <summary>Validates and persists incoming synchronized telemetry generations.</summary>
public interface ISessionTelemetryWriter
{
    /// <summary>
    /// Hub upload sink for a synced processed BLOB. The uploaded
    /// <paramref name="fingerprint"/> must ordinal-match the row's stored
    /// processing fingerprint, otherwise the bytes are not the ones the row is
    /// awaiting and an <see cref="System.IO.InvalidDataException"/> is thrown
    /// (the sync server maps it to 400). On a match it writes the BLOB, its
    /// fingerprint, and metrics recomputed from the new bytes (no `updated` bump).
    /// </summary>
    Task PatchSessionPsstAsync(Guid id, byte[] data, string? fingerprint);

    /// <summary>
    /// Commits a downloaded processed BLOB after the caller has already verified
    /// its fingerprint against the swap/fill target. It overwrites the
    /// row's BLOB and fingerprint coherently — with no concurrency re-check, so a
    /// swap can replace a held BLOB whose fingerprint differs from the new one —
    /// and recomputes metrics from the new bytes (no `updated` bump).
    /// </summary>
    Task SwapSessionPsstAsync(Guid id, byte[] data, string? fingerprint);

    Task SwapSessionPsstAsync(
        Guid id,
        byte[] data,
        string? fingerprint,
        SessionProcessedGeneration generation);
}

internal sealed class SessionTelemetryWriter(
    ISessionRepository sessionRepository,
    ISessionTelemetryProcessor sessionTelemetryProcessor) : ISessionTelemetryWriter
{
    public async Task PatchSessionPsstAsync(Guid id, byte[] data, string? fingerprint)
    {
        var current = await sessionRepository.GetSessionAsync(id)
                      ?? throw new Exception($"Session {id} does not exist.");

        // Reject bytes whose fingerprint is not the one this row's metadata
        // advertises: the hub asked for the BLOB matching its stored fingerprint,
        // so a mismatch is an integrity failure, not the awaited fill. The
        // sync server maps InvalidDataException to a 400 and keeps the row pending.
        if (!StringComparer.Ordinal.Equals(fingerprint, current.ProcessingFingerprintJson))
        {
            throw new InvalidDataException(
                $"Uploaded processed data for session {id} does not match the stored processing fingerprint.");
        }

        await WriteProcessedBytesAsync(id, data, fingerprint, current);
    }

    public async Task SwapSessionPsstAsync(Guid id, byte[] data, string? fingerprint)
    {
        var current = await sessionRepository.GetSessionAsync(id)
                      ?? throw new Exception($"Session {id} does not exist.");

        // The caller (sync client or load-time fill) already matched the downloaded
        // fingerprint against its target, so write through unconditionally — the
        // stored fingerprint may legitimately differ (a swap replaces a held BLOB).
        await WriteProcessedBytesAsync(id, data, fingerprint, current);
    }

    public async Task SwapSessionPsstAsync(
        Guid id,
        byte[] data,
        string? fingerprint,
        SessionProcessedGeneration generation)
    {
        _ = sessionTelemetryProcessor.ReadProcessedTelemetryData(data);
        await sessionRepository.UpdateSessionProcessedGenerationAsync(
            id,
            data,
            fingerprint,
            generation);
    }

    private async Task WriteProcessedBytesAsync(Guid id, byte[] data, string? fingerprint, Session current)
    {
        var telemetryData = sessionTelemetryProcessor.ReadProcessedTelemetryData(data);
        var track = await sessionRepository.GetSessionTrackAsync(id);

        var durationSeconds = telemetryData.Metadata?.Duration ?? current.DurationSeconds;
        var metrics = sessionTelemetryProcessor.ComputeSummaryMetrics(durationSeconds, track);
        var hasTrackPoints = track is { Count: > 0 };
        var finalMetrics = new SessionSummaryMetrics(
            metrics.DurationSeconds,
            hasTrackPoints ? metrics.DistanceMeters : current.DistanceMeters,
            hasTrackPoints ? metrics.AscentMeters : current.AscentMeters,
            hasTrackPoints ? metrics.DescentMeters : current.DescentMeters);

        await sessionRepository.UpdateSessionPsstAsync(id, data, fingerprint, finalMetrics);
    }
}
