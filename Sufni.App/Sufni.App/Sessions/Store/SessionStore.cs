using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.ExtensionHost.Contracts.Models;

using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Stores;
namespace Sufni.App.Sessions.Store;

/// <summary>
/// Single source of truth for "what sessions exist" (metadata only —
/// the psst blob lives in the database, not here). Loaded once at
/// startup and updated by coordinators via
/// <see cref="ISessionStoreWriter"/>.
/// </summary>
internal sealed class SessionStore(
    ISessionRepository sessionRepository,
    ISessionTelemetryWriter sessionTelemetryWriter,
    IUiThreadDispatcher uiThreadDispatcher)
    : SourceCacheStoreBase<SessionSnapshot, Guid>(s => s.Id, uiThreadDispatcher), ISessionStoreWriter
{
    public IObservable<SessionSnapshot> Watch(Guid id) =>
        WatchCore(id)
            // Skip Remove events: RefreshAsync clears and repopulates the
            // cache, so a watched session can briefly disappear before the
            // fresh Add arrives. The editor should react only to the current
            // Add/Update snapshot, not to that transient removal.
            .Where(c => c.Reason is ChangeReason.Add or ChangeReason.Update)
            .Select(c => c.Current);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sessions = await sessionRepository.GetSessionsAsync();
        cancellationToken.ThrowIfCancellationRequested();
        await ReplaceWithAsync(sessions.Select(SessionSnapshot.From));
    }

    public async Task<StoreMutationResult<SessionSnapshot>> CommitSessionMetadataAsync(
        Session session,
        long? baselineUpdated = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (baselineUpdated.HasValue)
        {
            var current = Get(session.Id);
            if (current is not null && current.Updated > baselineUpdated.Value)
            {
                return new StoreMutationResult<SessionSnapshot>.Conflict(current);
            }
        }

        try
        {
            await sessionRepository.PutSessionAsync(session);
            cancellationToken.ThrowIfCancellationRequested();
            return await PublishFreshSessionAsync(session.Id);
        }
        catch (Exception e)
        {
            return new StoreMutationResult<SessionSnapshot>.Failed(e.Message);
        }
    }

    public async Task<StoreMutationResult<SessionSnapshot>> CommitDerivedSessionAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await sessionRepository.PutSessionAsync(session);
            cancellationToken.ThrowIfCancellationRequested();
            return await PublishFreshSessionAsync(session.Id);
        }
        catch (Exception e)
        {
            return new StoreMutationResult<SessionSnapshot>.Failed(e.Message);
        }
    }

    public async Task<StoreMutationResult<SessionSnapshot>> CommitSessionMetadataFieldAsync(
        Guid sessionId,
        Func<Session, Session> metadataUpdate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = Get(sessionId);
        if (current is null)
        {
            return new StoreMutationResult<SessionSnapshot>.Missing("Session is missing.");
        }

        try
        {
            var session = metadataUpdate(current.ToMetadataEntity());
            return await CommitSessionMetadataAsync(session, current.Updated, cancellationToken);
        }
        catch (Exception e)
        {
            return new StoreMutationResult<SessionSnapshot>.Failed(e.Message);
        }
    }

    public async Task<StoreMutationResult<SessionSnapshot>> CommitPsstPatchAsync(
        Guid sessionId,
        byte[] data,
        string? fingerprint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await sessionTelemetryWriter.PatchSessionPsstAsync(sessionId, data, fingerprint);
            cancellationToken.ThrowIfCancellationRequested();
            return await PublishFreshSessionAsync(sessionId);
        }
        catch (Exception e)
        {
            return new StoreMutationResult<SessionSnapshot>.Failed(e.Message);
        }
    }

    public async Task<StoreMutationResult<SessionSnapshot>> CommitPsstSwapAsync(
        Guid sessionId,
        byte[] data,
        string? fingerprint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await sessionTelemetryWriter.SwapSessionPsstAsync(sessionId, data, fingerprint);
            cancellationToken.ThrowIfCancellationRequested();
            return await PublishFreshSessionAsync(sessionId);
        }
        catch (Exception e)
        {
            return new StoreMutationResult<SessionSnapshot>.Failed(e.Message);
        }
    }

    public async Task<StoreMutationResult<SessionSnapshot>> CommitTrackPatchAsync(
        Guid sessionId,
        List<TrackPoint> points,
        double? gpsOffsetSeconds = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await sessionTelemetryWriter.PatchSessionTrackAsync(sessionId, points, gpsOffsetSeconds);
            cancellationToken.ThrowIfCancellationRequested();
            return await PublishFreshSessionAsync(sessionId);
        }
        catch (Exception e)
        {
            return new StoreMutationResult<SessionSnapshot>.Failed(e.Message);
        }
    }

    public async Task PublishSessionsChangedAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default)
    {
        var snapshots = new List<SessionSnapshot>();
        var removedIds = new List<Guid>();

        foreach (var sessionId in sessionIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = await sessionRepository.GetSessionAsync(sessionId);
            if (session is null)
            {
                removedIds.Add(sessionId);
            }
            else
            {
                snapshots.Add(SessionSnapshot.From(session));
            }
        }

        if (snapshots.Count > 0)
        {
            await PublishSnapshotsAsync(snapshots);
        }

        if (removedIds.Count > 0)
        {
            await PublishRemovalsAsync(removedIds);
        }
    }

    public Task PublishSessionsRemovedAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PublishRemovalsAsync(sessionIds.Distinct());
    }

    private async Task<StoreMutationResult<SessionSnapshot>> PublishFreshSessionAsync(Guid sessionId)
    {
        var fresh = await sessionRepository.GetSessionAsync(sessionId);
        if (fresh is null)
        {
            return new StoreMutationResult<SessionSnapshot>.Missing("Session disappeared after save.");
        }

        var snapshot = SessionSnapshot.From(fresh);
        await PublishSnapshotAsync(snapshot);
        return new StoreMutationResult<SessionSnapshot>.Saved(snapshot);
    }

}
