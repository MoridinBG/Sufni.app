using System;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Sufni.Kinematics;

namespace Sufni.App.Coordinators;

/// <summary>
/// Owns the bike feature workflow: creating/editing, saving, deleting,
/// exporting. The coordinator is the only layer that writes to
/// <c>IBikeStoreWriter</c> and the only layer that decides post-save
/// navigation (e.g. pop the page on mobile).
/// </summary>
public interface IBikeCoordinator
{
    /// <summary>
    /// Open the editor for a new (unsaved) bike.
    /// </summary>
    Task OpenCreateAsync();

    /// <summary>
    /// Open the editor for an existing bike, hydrated from the store.
    /// No-op if the bike is not in the store.
    /// </summary>
    Task OpenEditAsync(Guid bikeId);

    /// <summary>
    /// Persist the editor's state. The coordinator checks
    /// <paramref name="baselineUpdated"/> against the store's current
    /// version and returns <see cref="BikeSaveOutcome.ConflictDetected"/>
    /// if another write has landed since the editor opened. On success
    /// it upserts the new snapshot into the store.
    /// </summary>
    Task<BikeSaveResult> SaveAsync(BikeEditorState state, long baselineUpdated);

    /// <summary>
    /// Delete a bike. Fails with <see cref="BikeDeleteOutcome.InUse"/>
    /// if <see cref="Queries.IBikeDependencyQuery"/> reports the bike is
    /// referenced by a setup.
    /// </summary>
    Task<BikeDeleteResult> DeleteAsync(Guid bikeId);

    /// <summary>
    /// True if the bike can be deleted right now. Used to enable/disable
    /// row-level delete commands; the authoritative check still happens
    /// inside <see cref="DeleteAsync"/>.
    /// </summary>
    Task<bool> CanDeleteAsync(Guid bikeId);
}

/// <summary>
/// Plain data DTO of the editor's current editable state. Built by
/// <c>BikeEditorViewModel</c> on save and handed to
/// <see cref="IBikeCoordinator.SaveAsync"/>. The coordinator never
/// touches the editor directly.
/// </summary>
public sealed record BikeEditorState(
    Guid Id,
    bool IsNew,
    string Name,
    double HeadAngle,
    double? ForkStroke,
    double? ShockStroke,
    double? Chainstay,
    double PixelsToMillimeters,
    Linkage? Linkage,
    Bitmap? Image);

public sealed record BikeSaveResult(BikeSaveOutcome Outcome, string? ErrorMessage = null, long NewBaselineUpdated = 0);

public enum BikeSaveOutcome
{
    Saved,
    ConflictDetected,
    Failed
}

public sealed record BikeDeleteResult(BikeDeleteOutcome Outcome, string? ErrorMessage = null);

public enum BikeDeleteOutcome
{
    Deleted,
    InUse,
    Failed
}
