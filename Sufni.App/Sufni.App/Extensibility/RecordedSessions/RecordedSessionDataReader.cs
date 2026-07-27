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
    ITrackRepository trackRepository,
    ISessionTelemetryProcessor sessionTelemetryProcessor,
    ISessionProcessedTelemetryReader processedTelemetryReader) : IRecordedSessionDataReader
{
    public async Task<IReadOnlyList<RecordedSessionCatalogItem>> GetSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sessions = await sessionRepository.GetSessionsAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var fullTrackIds = sessions
            .Where(session => session.FullTrack.HasValue)
            .Select(session => session.FullTrack!.Value)
            .Distinct()
            .ToArray();
        var fullTrackMetadata = await trackRepository.GetTrackPayloadMetadataByIdsAsync(fullTrackIds);
        cancellationToken.ThrowIfCancellationRequested();
        var fullTrackVersions = fullTrackMetadata.ToDictionary(item => item.Id, item => item.PointsRevision);

        return sessions
            .Select(session => CreateCatalogItem(
                session,
                session.FullTrack is { } fullTrackId && fullTrackVersions.TryGetValue(fullTrackId, out var updated)
                    ? updated
                    : null))
            .ToArray();
    }

    public async Task<RecordedSessionCatalogItem?> GetSessionAsync(
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

        TrackPayloadMetadata? fullTrackMetadata = null;
        if (session.FullTrack is { } fullTrackId)
        {
            fullTrackMetadata = await trackRepository.GetTrackPayloadMetadataAsync(fullTrackId);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return CreateCatalogItem(session, fullTrackMetadata?.PointsRevision);
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

        var track = await sessionTrackReader.GetSessionTrackAsync(sessionId, session.TrackProjectionRevision, cancellationToken);
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

    public async Task<RecordedSessionContentSnapshotResult> GetExactSnapshotAsync(
        Guid sessionId,
        RecordedSessionContentToken expectedToken,
        RecordedSessionContentSelection selection,
        CancellationToken cancellationToken = default)
    {
        if ((selection & ~RecordedSessionContentSelection.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selection));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var session = await sessionRepository.GetSessionAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        if (session is null)
        {
            return new RecordedSessionContentSnapshotResult.Missing();
        }

        var currentToken = await CreateContentTokenAsync(session, cancellationToken);
        if (currentToken != expectedToken)
        {
            return new RecordedSessionContentSnapshotResult.Stale();
        }

        TelemetryData? telemetry = null;
        if (selection.HasFlag(RecordedSessionContentSelection.ProcessedTelemetry))
        {
            telemetry = await processedTelemetryReader.GetExactAsync(
                sessionId,
                expectedToken.ProcessedTelemetryRevision,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (telemetry is null)
            {
                return await ClassifyUnavailableSnapshotAsync(sessionId, expectedToken, cancellationToken);
            }
        }

        IReadOnlyList<TrackPoint>? track = null;
        if (selection.HasFlag(RecordedSessionContentSelection.Track))
        {
            track = await GetExactTrackAsync(sessionId, session, expectedToken, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (track is null)
            {
                return await ClassifyUnavailableSnapshotAsync(sessionId, expectedToken, cancellationToken);
            }
        }

        var revalidatedSession = await sessionRepository.GetSessionAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        if (revalidatedSession is null)
        {
            return new RecordedSessionContentSnapshotResult.Missing();
        }

        if (await CreateContentTokenAsync(revalidatedSession, cancellationToken) != expectedToken)
        {
            return new RecordedSessionContentSnapshotResult.Stale();
        }

        return new RecordedSessionContentSnapshotResult.Available(
            new RecordedSessionContentSnapshot(expectedToken, selection, telemetry, track));
    }

    private async Task<RecordedSessionContentSnapshotResult> ClassifyUnavailableSnapshotAsync(
        Guid sessionId,
        RecordedSessionContentToken expectedToken,
        CancellationToken cancellationToken)
    {
        var currentSession = await sessionRepository.GetSessionAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        if (currentSession is null)
        {
            return new RecordedSessionContentSnapshotResult.Missing();
        }

        return await CreateContentTokenAsync(currentSession, cancellationToken) == expectedToken
            ? new RecordedSessionContentSnapshotResult.Missing()
            : new RecordedSessionContentSnapshotResult.Stale();
    }

    private async Task<IReadOnlyList<TrackPoint>?> GetExactTrackAsync(
        Guid sessionId,
        Session session,
        RecordedSessionContentToken expectedToken,
        CancellationToken cancellationToken)
    {
        var track = await sessionTrackReader.GetSessionTrackExactAsync(
            sessionId,
            expectedToken.TrackProjectionRevision,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (session.FullTrack is not { } fullTrackId)
        {
            return track;
        }

        if (SessionTrackProjection.IsSessionTrackAligned(track, session.Timestamp, session.GpsOffsetSeconds))
        {
            return track;
        }

        if (expectedToken.FullTrackPointsRevision is not { } fullTrackPointsRevision)
        {
            return null;
        }

        var fullTrackPoints = await fullTrackPointReader.GetTrackPointsExactAsync(
            fullTrackId,
            fullTrackPointsRevision,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (fullTrackPoints is null)
        {
            return null;
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

    private async Task<RecordedSessionContentToken> CreateContentTokenAsync(
        Session session,
        CancellationToken cancellationToken)
    {
        long? fullTrackPointsRevision = null;
        if (session.FullTrack is { } fullTrackId)
        {
            var metadata = await trackRepository.GetTrackPayloadMetadataAsync(fullTrackId);
            cancellationToken.ThrowIfCancellationRequested();
            fullTrackPointsRevision = metadata?.PointsRevision;
        }

        return CreateContentToken(session, fullTrackPointsRevision);
    }

    private static RecordedSessionCatalogItem CreateCatalogItem(
        Session session,
        long? fullTrackPointsRevision)
    {
        return new RecordedSessionCatalogItem(
            session.Id,
            session.Name,
            session.Timestamp,
            session.DurationSeconds,
            CreateContentToken(session, fullTrackPointsRevision));
    }

    private static RecordedSessionContentToken CreateContentToken(
        Session session,
        long? fullTrackPointsRevision) =>
        new(
            session.ProcessedTelemetryRevision,
            session.TrackProjectionRevision,
            session.FullTrack,
            fullTrackPointsRevision,
            session.Timestamp,
            session.DurationSeconds,
            session.GpsOffsetSeconds);
}
