using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.Telemetry;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Stores;
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
    ITrackStore trackStore,
    ISessionProcessedTelemetryReader processedTelemetryReader) : ISessionContentReader
{
    public async Task<IReadOnlyList<SessionCatalogItem>> GetSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sessions = await sessionRepository.GetSessionsAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var tracks = trackStore.GetAll();
        return sessions.Select(session => new SessionCatalogItem(session.Id, session.Name, session.Timestamp,
            session.DurationSeconds, CreateContentToken(session, tracks))).ToArray();
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

        return new SessionCatalogItem(session.Id, session.Name, session.Timestamp, session.DurationSeconds,
            CreateContentToken(session, trackStore.GetAll()));
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

        var currentToken = CreateContentToken(session, trackStore.GetAll());
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
            track = expectedToken.TrackKey is { } key
                ? await sessionTrackReader.GetSessionTrackAsync(key, cancellationToken)
                : null;
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

        if (CreateContentToken(revalidatedSession, trackStore.GetAll()) != expectedToken)
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

        return CreateContentToken(currentSession, trackStore.GetAll()) == expectedToken
            ? new SessionContentSnapshotResult.Missing()
            : new SessionContentSnapshotResult.Stale();
    }

    private static SessionContentToken CreateContentToken(Session session, IReadOnlyList<TrackSnapshot> tracks)
    {
        var selected = SessionTrackSelection.Select(tracks, session.Timestamp, session.DurationSeconds, session.GpsOffsetSeconds, session.WindowStartSeconds);
        return new SessionContentToken(session.ProcessedTelemetryRevision, selected?.Id, selected?.PointsRevision,
            session.Timestamp, session.DurationSeconds, SessionTrackTimeline.NormalizeGpsOffsetSeconds(session.GpsOffsetSeconds),
            WindowOriginFraction: SessionTrackTimeline.FractionalOrigin(session.WindowStartSeconds));
    }
}
