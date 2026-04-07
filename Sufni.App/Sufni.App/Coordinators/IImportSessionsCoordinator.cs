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
    /// </summary>
    Task<SessionImportResult> ImportAsync(
        ITelemetryDataStore dataStore,
        IReadOnlyList<ITelemetryFile> files,
        Guid setupId);
}

public sealed record SessionImportResult(
    IReadOnlyList<SessionSnapshot> Imported,
    IReadOnlyList<(string FileName, string ErrorMessage)> Failures);
