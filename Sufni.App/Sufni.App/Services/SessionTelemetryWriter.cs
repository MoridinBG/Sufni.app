using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Models;

namespace Sufni.App.Services;

/// <summary>
/// Owns the domain computation that precedes session persistence: telemetry
/// validation, summary-metric derivation, and session-window track
/// association/generation. Persists through <see cref="ISessionRepository"/>,
/// which stores values as given.
/// </summary>
public interface ISessionTelemetryWriter
{
    Task<Session> PutProcessedSessionAsync(Session session, Track? newFullTrack, RecordedSessionSource? source);

    Task<Session?> PutProcessedSessionIfUnchangedAsync(
        Session session,
        Track? newFullTrack,
        RecordedSessionSource? source,
        long baselineUpdated);

    Task PatchSessionPsstAsync(Guid id, byte[] data);

    Task PatchSessionTrackAsync(Guid id, List<TrackPoint> points, double? gpsOffsetSeconds = null);
}

internal sealed class SessionTelemetryWriter(
    ISessionRepository sessionRepository,
    ITrackRepository trackRepository,
    ISessionTelemetryProcessor sessionTelemetryProcessor) : ISessionTelemetryWriter
{
    public async Task<Session> PutProcessedSessionAsync(
        Session session,
        Track? newFullTrack,
        RecordedSessionSource? source)
    {
        await PrepareProcessedSessionAsync(session, newFullTrack);
        return await sessionRepository.PutProcessedSessionAsync(session, newFullTrack, source);
    }

    public async Task<Session?> PutProcessedSessionIfUnchangedAsync(
        Session session,
        Track? newFullTrack,
        RecordedSessionSource? source,
        long baselineUpdated)
    {
        await PrepareProcessedSessionAsync(session, newFullTrack);
        return await sessionRepository.PutProcessedSessionIfUnchangedAsync(session, newFullTrack, source, baselineUpdated);
    }

    public async Task PatchSessionPsstAsync(Guid id, byte[] data)
    {
        var telemetryData = sessionTelemetryProcessor.ReadProcessedTelemetryData(data);

        var current = await sessionRepository.GetSessionAsync(id)
                      ?? throw new Exception($"Session {id} does not exist.");
        var track = await sessionRepository.GetSessionTrackAsync(id);

        var durationSeconds = telemetryData.Metadata?.Duration ?? current.DurationSeconds;
        var metrics = sessionTelemetryProcessor.ComputeSummaryMetrics(durationSeconds, track);
        var hasTrackPoints = track is { Count: > 0 };
        var finalMetrics = new SessionSummaryMetrics(
            metrics.DurationSeconds,
            hasTrackPoints ? metrics.DistanceMeters : current.DistanceMeters,
            hasTrackPoints ? metrics.AscentMeters : current.AscentMeters,
            hasTrackPoints ? metrics.DescentMeters : current.DescentMeters);

        await sessionRepository.UpdateSessionPsstAsync(id, data, finalMetrics);
    }

    public async Task PatchSessionTrackAsync(Guid id, List<TrackPoint> points, double? gpsOffsetSeconds = null)
    {
        var current = await sessionRepository.GetSessionAsync(id)
                       ?? throw new Exception($"Session {id} does not exist.");
        var raw = await sessionRepository.GetSessionRawPsstAsync(id);

        var durationSeconds = sessionTelemetryProcessor.ReadProcessedDurationSeconds(raw) ?? current.DurationSeconds;
        var metrics = sessionTelemetryProcessor.ComputeSummaryMetrics(durationSeconds, points);

        await sessionRepository.UpdateSessionTrackAsync(id, points, metrics, gpsOffsetSeconds);
    }

    private async Task PrepareProcessedSessionAsync(Session session, Track? newFullTrack)
    {
        var telemetryData = session.ProcessedData is { } processedData
            ? sessionTelemetryProcessor.ReadProcessedTelemetryData(processedData)
            : throw new InvalidDataException("Processed session data is required.");

        if (newFullTrack is null && session.FullTrack is null && session.Timestamp.HasValue)
        {
            session.FullTrack = await trackRepository.FindTrackContainingTimestampAsync(session.Timestamp.Value);
        }

        var durationSeconds = telemetryData.Metadata?.Duration ?? session.DurationSeconds;
        var points = await ResolveMetricTrackPointsAsync(session, newFullTrack, durationSeconds);
        var metrics = sessionTelemetryProcessor.ComputeSummaryMetrics(durationSeconds, points);

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
