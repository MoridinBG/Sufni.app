using System;

using Sufni.App.Setups.Models;
namespace Sufni.App.Setups.Stores;

/// <summary>
/// Immutable view of a setup as currently known to the store.
/// </summary>
public sealed record SetupSnapshot(
    Guid Id,
    string Name,
    Guid BikeId,
    string? FrontSensorConfigurationJson,
    string? RearSensorConfigurationJson,
    long Updated)
{
    public static SetupSnapshot From(Setup setup) => new(
        setup.Id,
        setup.Name,
        setup.BikeId,
        setup.FrontSensorConfigurationJson,
        setup.RearSensorConfigurationJson,
        setup.Updated);
}
