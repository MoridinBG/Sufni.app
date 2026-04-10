using System;
using Sufni.App.Models;

namespace Sufni.App.Stores;

/// <summary>
/// Immutable view of a setup as currently known to the store.
/// Includes the associated <see cref="BoardId"/> resolved from the
/// <c>Board</c> table at refresh time, so consumers do not need to
/// query boards separately.
/// </summary>
public sealed record SetupSnapshot(
    Guid Id,
    string Name,
    Guid BikeId,
    Guid? BoardId,
    string? FrontSensorConfigurationJson,
    string? RearSensorConfigurationJson,
    long Updated) : IVersionedSnapshot
{
    public static SetupSnapshot From(Setup setup, Guid? boardId) => new(
        setup.Id,
        setup.Name,
        setup.BikeId,
        boardId,
        setup.FrontSensorConfigurationJson,
        setup.RearSensorConfigurationJson,
        setup.Updated);
}
