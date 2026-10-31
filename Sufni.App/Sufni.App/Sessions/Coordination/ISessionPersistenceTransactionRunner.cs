using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Sessions.Store;
using Sufni.App.Video.Models;
namespace Sufni.App.Sessions.Coordination;

public interface ISessionPersistenceTransactionRunner
{
    /// <summary>
    /// Deletes the session, its unshared track and its unreferenced recording
    /// source in one transaction. Returns the removals to publish.
    /// </summary>
    Task<SessionDeleteCommit> DeleteSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    // The edit commands take the session as the caller saw it and reject the
    // edit when the stored window has changed since. Click seconds are relative
    // to the start of the session's current window.

    Task<SessionEditCommit> TrimUntilEndAsync(SessionSnapshot session, double clickSeconds);

    /// <summary>
    /// Moves the window start and the session origin to the click. The session's
    /// segments keep their place in the recording, and its video alignment shifts
    /// by the change of the origin's fractional second.
    /// </summary>
    Task<SessionEditCommit> TrimFromBeginningAsync(SessionSnapshot session, double clickSeconds);

    Task<SessionEditCommit> UndoTrimAsync(SessionSnapshot session);

    /// <summary>
    /// Ends the session's window at the click, renames it "(1)", and creates a
    /// "(2)" session over the rest of the window with copies of its video clips.
    /// Source-owned segments remain unchanged.
    /// </summary>
    Task<SessionEditCommit> SplitAsync(SessionSnapshot session, double clickSeconds);
}

public abstract record SessionEditCommit
{
    private SessionEditCommit() { }

    public sealed record Committed(
        IReadOnlyList<Guid> SessionIds,
        IReadOnlyList<SessionVideoClip> Clips,
        Guid? CreatedSessionId = null) : SessionEditCommit;

    public sealed record Rejected(string Message) : SessionEditCommit;
}

public readonly record struct SessionDeleteCommit(bool SourceDeleted, Guid? RemovedTrackId);
