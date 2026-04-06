using System;
using System.Threading.Tasks;

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
    /// Persist the editor's state. The coordinator checks
    /// <paramref name="baselineUpdated"/> against the store's current
    /// version and returns <see cref="SetupSaveOutcome.ConflictDetected"/>
    /// if another write has landed since the editor opened. On success
    /// it persists the setup, fixes up the board association, and
    /// upserts the new snapshot into the store.
    /// </summary>
    Task<SetupSaveResult> SaveAsync(SetupEditorState state, long baselineUpdated);

    /// <summary>
    /// Delete a setup. Clears the board association first if any.
    /// </summary>
    Task<SetupDeleteResult> DeleteAsync(Guid setupId);
}

/// <summary>
/// Plain data DTO of the setup editor's current editable state. Built
/// by <c>SetupEditorViewModel</c> on save and handed to
/// <see cref="ISetupCoordinator.SaveAsync"/>. The coordinator never
/// touches the editor directly.
///
/// <see cref="OriginalBoardId"/> is the board the setup was associated
/// with at the time the editor opened — the coordinator needs it to
/// clear the old association if the user reassigned the board.
/// </summary>
public sealed record SetupEditorState(
    Guid Id,
    bool IsNew,
    string Name,
    Guid BikeId,
    Guid? BoardId,
    Guid? OriginalBoardId,
    string? FrontSensorConfigurationJson,
    string? RearSensorConfigurationJson);

public sealed record SetupSaveResult(SetupSaveOutcome Outcome, string? ErrorMessage = null, long NewBaselineUpdated = 0);

public enum SetupSaveOutcome
{
    Saved,
    ConflictDetected,
    Failed
}

public sealed record SetupDeleteResult(SetupDeleteOutcome Outcome, string? ErrorMessage = null);

public enum SetupDeleteOutcome
{
    Deleted,
    Failed
}
