using System;
using Sufni.App.Models;

namespace Sufni.App.Stores;

/// <summary>
/// Immutable view of a paired device as currently known to the store.
/// Keyed by <see cref="DeviceId"/> (the <c>paired_device</c> table's
/// primary key) — paired devices have no <see cref="Guid"/> identity,
/// so the store deviates from the bike/setup/session pattern and uses
/// <see cref="string"/> as its key type.
/// </summary>
public sealed record PairedDeviceSnapshot(
    string DeviceId,
    string? DisplayName,
    DateTime Expires)
{
    public static PairedDeviceSnapshot From(PairedDevice device) =>
        new(device.DeviceId, device.DisplayName, device.Expires);
}
