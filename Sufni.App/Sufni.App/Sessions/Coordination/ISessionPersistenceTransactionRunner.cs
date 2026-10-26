using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Segments.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Video.Models;
namespace Sufni.App.Sessions.Coordination;

public interface ISessionPersistenceTransactionRunner
{
    /// <summary>
    /// Deletes the session and, when no other live session derives from it, its
    /// recording source. Returns true when the recording source was deleted.
    /// </summary>
    Task<bool> DeleteSessionAsync(
        Guid sessionId,
        Guid? fullTrackId,
        bool deleteFullTrack,
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
    /// Segments that start at or after the click move to the new session.
    /// </summary>
    Task<SessionEditCommit> SplitAsync(SessionSnapshot session, double clickSeconds);
}

public abstract record SessionEditCommit
{
    private SessionEditCommit() { }

    public sealed record Committed(
        IReadOnlyList<Guid> SessionIds,
        IReadOnlyList<SessionVideoClip> Clips,
        IReadOnlyList<SessionSegment> Segments,
        Guid? CreatedSessionId = null) : SessionEditCommit;

    public sealed record Rejected(string Message) : SessionEditCommit;
}
