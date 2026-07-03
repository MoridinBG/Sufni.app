using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

/// <summary>
/// Neutral host operations for extensions that derive recorded sessions from
/// recording-source windows. The host owns session creation, metadata updates,
/// recomputation, and navigation; extensions own their own workflow semantics.
/// </summary>
public interface IRecordedSessionEditingOperations
{
    Task<Guid?> CreateDerivedSessionAsync(
        Guid fromSessionId,
        string name,
        double sourceAbsoluteStartSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-anchors an existing session's timestamp and GPS offset to a
    /// source-absolute position. Callers that also mutate a derivation window
    /// must invoke this before changing the window so the host can read a
    /// consistent session/window pair.
    /// </summary>
    Task<bool> UpdateSessionOriginAsync(
        Guid sessionId,
        double sourceAbsoluteStartSeconds,
        CancellationToken cancellationToken = default);

    Task<bool> RenameSessionAsync(
        Guid sessionId,
        string name,
        CancellationToken cancellationToken = default);

    Task<bool> RequestRecomputeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task OpenSessionInBackgroundAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
