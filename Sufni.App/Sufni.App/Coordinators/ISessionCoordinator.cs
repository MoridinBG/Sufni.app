using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.App.Stores;

namespace Sufni.App.Coordinators;

/// <summary>
/// Owns the session feature workflow: opening the detail editor,
/// saving, deleting, and the local mobile telemetry-fetch path. The
/// coordinator is the only layer that writes to
/// <see cref="ISessionStoreWriter"/> and the only layer that decides
/// post-save navigation.
/// </summary>
public interface ISessionCoordinator
{
    /// <summary>
    /// Open the detail editor for an existing session, hydrated from
    /// the store. No-op if the session is not in the store.
    /// </summary>
    Task OpenEditAsync(Guid sessionId);

    /// <summary>
    /// Load the data required by the desktop session-detail experience.
    /// Returns explicit outcomes for loaded, telemetry-pending, and
    /// failed states so the editor can apply the result in one pass.
    /// </summary>
    Task<SessionDesktopLoadResult> LoadDesktopDetailAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Load the data required by the mobile session-detail experience.
    /// Uses the cache when available, otherwise ensures telemetry is
    /// available, builds/persists the cache, and returns an explicit
    /// outcome for the editor to apply.
    /// </summary>
    Task<SessionMobileLoadResult> LoadMobileDetailAsync(
        Guid sessionId,
        SessionPresentationDimensions dimensions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist a session built by the editor. The coordinator checks
    /// <paramref name="baselineUpdated"/> against the store's current
    /// version and returns <see cref="SessionSaveResult.Conflict"/> if
    /// another write has landed since the editor opened. On success it
    /// upserts the new snapshot into the store.
    /// </summary>
    Task<SessionSaveResult> SaveAsync(Session session, long baselineUpdated);

    /// <summary>
    /// Delete a session.
    /// </summary>
    Task<SessionDeleteResult> DeleteAsync(Guid sessionId);

    /// <summary>
    /// Mobile telemetry-fetch path. If the snapshot's
    /// <c>HasProcessedData</c> is already true, returns immediately.
    /// Otherwise downloads the psst blob via <c>IHttpApiService</c>,
    /// patches the local database, re-fetches the row through the
    /// SQL-computed <c>has_data</c> path, and upserts the new
    /// snapshot. The editor's <c>Watch</c> subscription will then
    /// fire and trigger a telemetry reload — but during initial
    /// load the editor short-circuits the Watch handler so this
    /// method's caller is responsible for calling
    /// <c>LoadTelemetryData</c> directly afterwards.
    /// </summary>
    Task EnsureTelemetryDataAvailableAsync(Guid sessionId);
}

/// <summary>
/// Outcome of <see cref="ISessionCoordinator.SaveAsync"/>. Sealed
/// hierarchy with a private constructor so callers must pattern-match
/// on the three known cases.
/// </summary>
public abstract record SessionSaveResult
{
    private SessionSaveResult() { }

    public sealed record Saved(long NewBaselineUpdated) : SessionSaveResult;
    public sealed record Conflict(SessionSnapshot CurrentSnapshot) : SessionSaveResult;
    public sealed record Failed(string ErrorMessage) : SessionSaveResult;
}

public sealed record SessionDeleteResult(SessionDeleteOutcome Outcome, string? ErrorMessage = null);

public enum SessionDeleteOutcome
{
    Deleted,
    Failed
}
