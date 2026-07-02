using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Models;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
namespace Sufni.App.Sessions.Processing.Services;

/// <summary>
/// Owns the domain computation that precedes session persistence: telemetry
/// validation, summary-metric derivation, and session-window track
/// association/generation. Persists through <see cref="ISessionRepository"/>,
/// which stores values as given.
/// </summary>
public interface ISessionTelemetryWriter
{
    Task<Session> PutProcessedSessionAsync(
        Session session,
        ProcessedTelemetryPayload payload,
        Track? newFullTrack,
        RecordedSessionSource? source);

    /// <summary>
    /// Derived-only processed write used by the recompute engine. Prepares the
    /// session (summary metrics + cached session-window track) and delegates to
    /// <see cref="ISessionRepository.UpdateProcessedDerivedDataAsync"/>, which
    /// re-checks the DB-input fingerprint inside the write transaction and returns
    /// null on a passive dependency change (the engine re-enqueues). On success the
    /// session cache is invalidated so the next mobile load rebuilds from the fresh
    /// derived data.
    /// </summary>
    Task<Session?> UpdateProcessedDerivedDataAsync(
        Session session,
        ProcessedTelemetryPayload payload,
        Track? newFullTrack,
        ProcessingFingerprint expectedInputFingerprint);

    /// <summary>
    /// Hub upload sink for a synced processed BLOB. The uploaded
    /// <paramref name="fingerprint"/> must ordinal-match the row's stored
    /// processing fingerprint, otherwise the bytes are not the ones the row is
    /// awaiting and an <see cref="System.IO.InvalidDataException"/> is thrown
    /// (the sync server maps it to 400). On a match it writes the BLOB, its
    /// fingerprint, and metrics recomputed from the new bytes (no `updated`
    /// bump), then invalidates the session cache.
    /// </summary>
    Task PatchSessionPsstAsync(Guid id, byte[] data, string? fingerprint);

    /// <summary>
    /// Commits a downloaded processed BLOB after the caller has already verified
    /// its fingerprint against the swap/fill target. It overwrites the
    /// row's BLOB and fingerprint coherently — with no concurrency re-check, so a
    /// swap can replace a held BLOB whose fingerprint differs from the new one —
    /// recomputes metrics from the new bytes (no `updated` bump), and invalidates
    /// the session cache.
    /// </summary>
    Task SwapSessionPsstAsync(Guid id, byte[] data, string? fingerprint);

    Task PatchSessionTrackAsync(Guid id, List<TrackPoint> points, double? gpsOffsetSeconds = null);
}

internal sealed class SessionTelemetryWriter(
    ISessionRepository sessionRepository,
    ITrackRepository trackRepository,
    ISessionTelemetryProcessor sessionTelemetryProcessor,
    ISessionCacheStore sessionCacheStore) : ISessionTelemetryWriter
{
    public async Task<Session> PutProcessedSessionAsync(
        Session session,
        ProcessedTelemetryPayload payload,
        Track? newFullTrack,
        RecordedSessionSource? source)
    {
        await PrepareProcessedSessionAsync(session, payload, newFullTrack);
        return await sessionRepository.PutProcessedSessionAsync(session, newFullTrack, source);
    }

    public async Task<Session?> UpdateProcessedDerivedDataAsync(
        Session session,
        ProcessedTelemetryPayload payload,
        Track? newFullTrack,
        ProcessingFingerprint expectedInputFingerprint)
    {
        await PrepareProcessedSessionAsync(session, payload, newFullTrack);
        var fresh = await sessionRepository.UpdateProcessedDerivedDataAsync(session, newFullTrack, expectedInputFingerprint);
        if (fresh is not null)
        {
            // The derived write changed session.data and the cached track; drop the
            // mobile session_cache so the next load rebuilds from the fresh data.
            await sessionCacheStore.DeleteSessionCacheAsync(session.Id);
        }

        return fresh;
    }

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

        // The cached presentation derives from the previous BLOB; drop it so the
        // next mobile load rebuilds from the freshly written bytes.
        await sessionCacheStore.DeleteSessionCacheAsync(id);
    }

    public async Task PatchSessionTrackAsync(Guid id, List<TrackPoint> points, double? gpsOffsetSeconds = null)
    {
        var current = await sessionRepository.GetSessionAsync(id)
                       ?? throw new Exception($"Session {id} does not exist.");

        var metrics = sessionTelemetryProcessor.ComputeSummaryMetrics(current.DurationSeconds, points);

        await sessionRepository.UpdateSessionTrackAsync(id, points, metrics, gpsOffsetSeconds);

        // The cached presentation derives from the session-window track and its
        // metrics; a track/GPS-offset change makes it stale, so drop it (like the
        // BLOB write paths) and let the next mobile load rebuild it.
        await sessionCacheStore.DeleteSessionCacheAsync(id);
    }

    private async Task PrepareProcessedSessionAsync(
        Session session,
        ProcessedTelemetryPayload payload,
        Track? newFullTrack)
    {
        var telemetryData = payload.TelemetryData;
        session.ProcessedData = payload.Data;
        session.ProcessingFingerprintJson = payload.FingerprintJson;

        if (newFullTrack is null && session.FullTrack is null && session.Timestamp.HasValue)
        {
            session.FullTrack = await trackRepository.FindTrackContainingTimestampAsync(session.Timestamp.Value);
        }

        var durationSeconds = telemetryData.Metadata?.Duration ?? session.DurationSeconds;
        var points = await ResolveMetricTrackPointsAsync(session, newFullTrack, durationSeconds);
        var metrics = sessionTelemetryProcessor.ComputeSummaryMetrics(durationSeconds, points);

        // Persist the cached session-window polyline at derivation time so the
        // processed write stores it directly. Import, live-save, and recompute all
        // flow through here, replacing the old reliance on a later lazy load-path
        // patch to materialize the `track` column.
        if (points is not null)
        {
            session.Track = points.ToList();
        }

        session.DurationSeconds = metrics.DurationSeconds;
        session.DistanceMeters = metrics.DistanceMeters;
        session.AscentMeters = metrics.AscentMeters;
        session.DescentMeters = metrics.DescentMeters;
    }

    private async Task<IReadOnlyList<TrackPoint>?> ResolveMetricTrackPointsAsync(
        Session session,
        Track? newFullTrack,
        double? durationSeconds)
    {
        if (session.Track is { Count: > 0 })
        {
            return session.Track;
        }

        if (newFullTrack?.Points is { Count: > 0 } generatedPoints)
        {
            return generatedPoints;
        }

        if (!session.FullTrack.HasValue ||
            !session.Timestamp.HasValue ||
            durationSeconds is not { } duration ||
            !double.IsFinite(duration) ||
            duration <= 0)
        {
            return null;
        }

        var tracks = await trackRepository.GetTracksByIdsAsync([session.FullTrack.Value]);
        if (tracks.Count == 0)
        {
            return null;
        }

        return sessionTelemetryProcessor.GenerateSessionTrackFromFullTrack(
            tracks[0],
            session.Timestamp,
            durationSeconds,
            session.GpsOffsetSeconds);
    }
}
