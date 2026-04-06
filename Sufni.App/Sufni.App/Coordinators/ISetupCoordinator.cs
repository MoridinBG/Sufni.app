using System;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Stores;

namespace Sufni.App.Coordinators;

/// <summary>
/// Owns the setup feature workflow: creating/editing, saving, deleting.
/// The coordinator is the only layer that writes to
/// <c>ISetupStoreWriter</c> and the only layer that owns the
/// <c>Board</c> row association — clearing the previous board on save
/// and on delete, then setting the new one.
/// </summary>
public interface ISetupCoordinator
{
    /// <summary>
    /// Open the editor for a new (unsaved) setup. The optional
    /// <paramref name="suggestedBoardId"/> is used to pre-populate the
    /// board association — typically from the currently selected
    /// telemetry datastore at import time.
    /// </summary>
    Task OpenCreateAsync(Guid? suggestedBoardId = null);

    /// <summary>
    /// Open the editor for an existing setup, hydrated from the store.
    /// No-op if the setup is not in the store.
    /// </summary>
    Task OpenEditAsync(Guid setupId);

    /// <summary>
    /// Persist a setup built by the editor along with the desired
    /// board association. The coordinator checks
    /// <paramref name="baselineUpdated"/> against the store's current
    /// version and returns <see cref="SetupSaveResult.Conflict"/> if
    /// another write has landed since the editor opened. On success it
    /// persists the setup, fixes up the board association (clearing
    /// the previously stored board if it changed), and upserts the new
    /// snapshot into the store.
    /// </summary>
    Task<SetupSaveResult> SaveAsync(Setup setup, Guid? boardId, long baselineUpdated);

    /// <summary>
    /// Delete a setup. Clears the board association first if any.
    /// </summary>
    Task<SetupDeleteResult> DeleteAsync(Guid setupId);
}

/// <summary>
/// Outcome of <see cref="ISetupCoordinator.SaveAsync"/>. Sealed
/// hierarchy with a private constructor so callers must pattern-match
/// on the three known cases.
/// </summary>
public abstract record SetupSaveResult
{
    private SetupSaveResult() { }

    public sealed record Saved(long NewBaselineUpdated) : SetupSaveResult;
    public sealed record Conflict(SetupSnapshot CurrentSnapshot) : SetupSaveResult;
    public sealed record Failed(string ErrorMessage) : SetupSaveResult;
}

public sealed record SetupDeleteResult(SetupDeleteOutcome Outcome, string? ErrorMessage = null);

public enum SetupDeleteOutcome
{
    Deleted,
    Failed
}
