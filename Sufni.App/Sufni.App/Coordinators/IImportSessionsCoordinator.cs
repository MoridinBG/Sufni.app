using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Stores;

namespace Sufni.App.Coordinators;

/// <summary>
/// Owns the import-sessions feature workflow: opening the import view
/// and running the per-file import lifecycle (psst generation,
/// database write, store upsert, trash for unimported files).
/// </summary>
public interface IImportSessionsCoordinator
{
    /// <summary>
    /// Open the import view. There is exactly one
    /// <c>ImportSessionsViewModel</c> singleton — this resolves it
    /// and routes through <see cref="IShellCoordinator.OpenOrFocus"/>.
    /// </summary>
    Task OpenAsync();

    /// <summary>
    /// Run the per-file import / trash lifecycle for the supplied
    /// files against the supplied data store and selected setup.
    /// Files with <c>ShouldBeImported == true</c> are imported;
    /// files with <c>ShouldBeImported == null</c> are trashed; files
    /// with <c>ShouldBeImported == false</c> are left alone (matching
    /// legacy semantics). Returns per-file successes and failures so
    /// the caller can render notifications.
    ///
    /// If <paramref name="progress"/> is supplied, per-file events are
    /// reported as soon as each file completes so the caller can
    /// stream notifications instead of batching them after the entire
    /// import finishes.
    /// </summary>
    Task<SessionImportResult> ImportAsync(
        ITelemetryDataStore dataStore,
        IReadOnlyList<ITelemetryFile> files,
        Guid setupId,
        IProgress<SessionImportEvent>? progress = null);
}

public sealed record SessionImportResult(
    IReadOnlyList<SessionSnapshot> Imported,
    IReadOnlyList<(string FileName, string ErrorMessage)> Failures);

/// <summary>
/// Per-file event reported during a streaming import. Sealed
/// hierarchy with a private constructor so callers must pattern-match
/// on the two known cases.
/// </summary>
public abstract record SessionImportEvent
{
    private SessionImportEvent() { }

    public sealed record Imported(SessionSnapshot Snapshot) : SessionImportEvent;
    public sealed record Failed(string FileName, string ErrorMessage) : SessionImportEvent;
}
