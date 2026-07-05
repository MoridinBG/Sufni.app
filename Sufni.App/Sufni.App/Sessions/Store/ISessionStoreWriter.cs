using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Stores;

namespace Sufni.App.Sessions.Store;

/// <summary>
/// Write surface for the session store. Convention: only the
/// composition root and coordinators should take a dependency on this
/// interface. View models, rows and queries take
/// <see cref="ISessionStore"/> instead.
/// </summary>
public interface ISessionStoreWriter : ISessionStore
{
    /// <summary>
    /// Load all sessions from the database and replace the current
    /// contents.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task<StoreMutationResult<SessionSnapshot>> CommitSessionMetadataAsync(
        Session session,
        long? baselineUpdated = null,
        CancellationToken cancellationToken = default);

    Task<StoreMutationResult<SessionSnapshot>> CommitDerivedSessionAsync(
        Session session,
        CancellationToken cancellationToken = default);

    Task<StoreMutationResult<SessionSnapshot>> CommitSessionMetadataFieldAsync(
        Guid sessionId,
        Func<Session, Session> metadataUpdate,
        CancellationToken cancellationToken = default);

    Task<StoreMutationResult<SessionSnapshot>> CommitPsstSwapAsync(
        Guid sessionId,
        byte[] data,
        string? fingerprint,
        CancellationToken cancellationToken = default);

    Task<StoreMutationResult<SessionSnapshot>> CommitTrackPatchAsync(
        Guid sessionId,
        List<TrackPoint> points,
        double? gpsOffsetSeconds = null,
        CancellationToken cancellationToken = default);

    Task PublishSessionsChangedAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default);

    Task PublishSessionsRemovedAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default);
}
