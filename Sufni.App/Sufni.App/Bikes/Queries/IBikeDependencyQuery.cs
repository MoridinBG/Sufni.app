using System;
using System.Reactive;

namespace Sufni.App.Bikes.Queries;

/// <summary>
/// Answers business questions about bike references held by other
/// entities.
/// </summary>
public interface IBikeDependencyQuery
{
    /// <summary>
    /// True if any published setup references the bike. Drives command
    /// availability only; the bike delete transaction makes the authoritative check.
    /// </summary>
    bool IsBikeInUse(Guid bikeId);

    /// <summary>
    /// Fires whenever the result of <see cref="IsBikeInUse"/> may have
    /// changed for any bike. Subscribers (typically delete-aware row
    /// or editor view models) refresh their <c>CanExecute</c> state in
    /// response. Emits an initial value on subscribe so subscribers can
    /// bootstrap from current state without a separate priming call.
    /// </summary>
    IObservable<Unit> Changes { get; }
}
