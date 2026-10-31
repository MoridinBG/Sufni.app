using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Sessions.Store;
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
    /// source-owned attachments keep their place in the recording.
    /// </summary>
    Task<SessionEditCommit> TrimFromBeginningAsync(SessionSnapshot session, double clickSeconds);

    Task<SessionEditCommit> UndoTrimAsync(SessionSnapshot session);

    /// <summary>
    /// Ends the session's window at the click, renames it "(1)", and creates a
    /// "(2)" session over the rest of the window.
    /// Source-owned segments remain unchanged.
    /// </summary>
    Task<SessionEditCommit> SplitAsync(SessionSnapshot session, double clickSeconds);
}

public abstract record SessionEditCommit
{
    private SessionEditCommit() { }

    public sealed record Committed(
        IReadOnlyList<Guid> SessionIds,
        Guid? CreatedSessionId = null) : SessionEditCommit;

    public sealed record Rejected(string Message) : SessionEditCommit;
}

public readonly record struct SessionDeleteCommit(bool SourceDeleted, Guid? RemovedTrackId);
