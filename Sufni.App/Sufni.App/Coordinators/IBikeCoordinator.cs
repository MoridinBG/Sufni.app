using System;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Stores;

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
    /// Persist a bike built by the editor. The coordinator checks
    /// <paramref name="baselineUpdated"/> against the store's current
    /// version and returns <see cref="BikeSaveResult.Conflict"/> if
    /// another write has landed since the editor opened. On success it
    /// upserts the new snapshot into the store.
    /// </summary>
    Task<BikeSaveResult> SaveAsync(Bike bike, long baselineUpdated);

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
/// Outcome of <see cref="IBikeCoordinator.SaveAsync"/>. Sealed
/// hierarchy with a private constructor so callers must pattern-match
/// on the three known cases.
/// </summary>
public abstract record BikeSaveResult
{
    private BikeSaveResult() { }

    public sealed record Saved(long NewBaselineUpdated) : BikeSaveResult;
    public sealed record Conflict(BikeSnapshot CurrentSnapshot) : BikeSaveResult;
    public sealed record Failed(string ErrorMessage) : BikeSaveResult;
}

public sealed record BikeDeleteResult(BikeDeleteOutcome Outcome, string? ErrorMessage = null);

public enum BikeDeleteOutcome
{
    Deleted,
    InUse,
    Failed
}
