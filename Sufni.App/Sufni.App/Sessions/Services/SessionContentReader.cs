using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Models;
namespace Sufni.App.Sessions.Services;

/// <summary>
/// Reads sessions, tracks, and processed telemetry for work across sessions.
/// Exact reads return the content of a given token or report it stale or missing.
/// </summary>
public interface ISessionContentReader
{
    Task<IReadOnlyList<SessionCatalogItem>> GetSessionsAsync(CancellationToken cancellationToken = default);

    Task<SessionCatalogItem?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<SessionContentSnapshotResult> GetExactSnapshotAsync(
        Guid sessionId,
        SessionContentToken expectedToken,
        SessionContentSelection selection,
        CancellationToken cancellationToken = default);
}

internal sealed class SessionContentReader(
    ISessionRepository sessionRepository,
    ISessionTrackReader sessionTrackReader,
    IFullTrackPointReader fullTrackPointReader,
    ITrackRepository trackRepository,
    ISessionTelemetryProcessor sessionTelemetryProcessor,
    ISessionProcessedTelemetryReader processedTelemetryReader) : ISessionContentReader
{
    public async Task<IReadOnlyList<SessionCatalogItem>> GetSessionsAsync(
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

    public async Task<SessionCatalogItem?> GetSessionAsync(
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

    public async Task<SessionContentSnapshotResult> GetExactSnapshotAsync(
        Guid sessionId,
        SessionContentToken expectedToken,
        SessionContentSelection selection,
        CancellationToken cancellationToken = default)
    {
        if ((selection & ~SessionContentSelection.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selection));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var session = await sessionRepository.GetSessionAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        if (session is null)
        {
            return new SessionContentSnapshotResult.Missing();
        }

        var currentToken = await CreateContentTokenAsync(session, cancellationToken);
        if (currentToken != expectedToken)
        {
            return new SessionContentSnapshotResult.Stale();
        }

        TelemetryData? telemetry = null;
        if (selection.HasFlag(SessionContentSelection.ProcessedTelemetry))
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
        if (selection.HasFlag(SessionContentSelection.Track))
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
            return new SessionContentSnapshotResult.Missing();
        }

        if (await CreateContentTokenAsync(revalidatedSession, cancellationToken) != expectedToken)
        {
            return new SessionContentSnapshotResult.Stale();
        }

        return new SessionContentSnapshotResult.Available(
            new SessionContentSnapshot(expectedToken, selection, telemetry, track));
    }

    private async Task<SessionContentSnapshotResult> ClassifyUnavailableSnapshotAsync(
        Guid sessionId,
        SessionContentToken expectedToken,
        CancellationToken cancellationToken)
    {
        var currentSession = await sessionRepository.GetSessionAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        if (currentSession is null)
        {
            return new SessionContentSnapshotResult.Missing();
        }

        return await CreateContentTokenAsync(currentSession, cancellationToken) == expectedToken
            ? new SessionContentSnapshotResult.Missing()
            : new SessionContentSnapshotResult.Stale();
    }

    private async Task<IReadOnlyList<TrackPoint>?> GetExactTrackAsync(
        Guid sessionId,
        Session session,
        SessionContentToken expectedToken,
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

    private async Task<SessionContentToken> CreateContentTokenAsync(
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

    private static SessionCatalogItem CreateCatalogItem(
        Session session,
        long? fullTrackPointsRevision)
    {
        return new SessionCatalogItem(
            session.Id,
            session.Name,
            session.Timestamp,
            session.DurationSeconds,
            CreateContentToken(session, fullTrackPointsRevision));
    }

    private static SessionContentToken CreateContentToken(
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
