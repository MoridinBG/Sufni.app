using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Models;
namespace Sufni.App.Extensibility.RecordedSessions;

internal sealed class RecordedSessionDataReader(
    ISessionRepository sessionRepository,
    ISessionTrackReader sessionTrackReader,
    IFullTrackPointReader fullTrackPointReader,
    ISessionTelemetryProcessor sessionTelemetryProcessor,
    ISessionProcessedTelemetryReader processedTelemetryReader) : IRecordedSessionDataReader
{
    public async Task<IReadOnlyList<RecordedSessionCatalogItem>> GetSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sessions = await sessionRepository.GetSessionsAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return sessions
            .Select(session => new RecordedSessionCatalogItem(
                session.Id,
                session.Name,
                session.Timestamp,
                session.DurationSeconds))
            .ToArray();
    }

    public async Task<RecordedSessionCatalogItem?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = await sessionRepository.GetSessionAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        return session is null
            ? null
            : new RecordedSessionCatalogItem(
                session.Id,
                session.Name,
                session.Timestamp,
                session.DurationSeconds);
    }

    public async Task<TelemetryData?> GetProcessedTelemetryAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        return await processedTelemetryReader.GetAsync(sessionId, cancellationToken);
    }

    public async Task<IReadOnlyList<TrackPoint>?> GetTrackAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = await sessionRepository.GetSessionAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        if (session is null)
        {
            return null;
        }

        var track = await sessionTrackReader.GetSessionTrackAsync(sessionId, session.Updated, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (session.FullTrack is not { } fullTrackId)
        {
            return track;
        }

        // The cached session-window track is generated from the session row's own
        // timestamp and duration, so align and regenerate against those instead of
        // deserializing the full processed-telemetry blob per session — matching
        // enumerates every session, so a per-session blob decode dominated the scan.
        if (SessionTrackProjection.IsSessionTrackAligned(track, session.Timestamp, session.GpsOffsetSeconds))
        {
            return track;
        }

        var fullTrackPoints = await fullTrackPointReader.GetTrackPointsAsync(fullTrackId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (fullTrackPoints is null)
        {
            return track;
        }

        var fullTrack = new Track
        {
            Id = fullTrackId,
            Points = fullTrackPoints as List<TrackPoint> ?? fullTrackPoints.ToList(),
        };
        return sessionTelemetryProcessor.GenerateSessionTrackFromFullTrack(
            fullTrack,
            session.Timestamp,
            session.DurationSeconds,
            session.GpsOffsetSeconds) ?? track;
    }
}
