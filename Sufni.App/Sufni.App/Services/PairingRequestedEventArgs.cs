using System;

namespace Sufni.App.Services;

public sealed class PairingRequestedEventArgs(string deviceId, string? displayName, string pin) : EventArgs
{
    public string DeviceId { get; } = deviceId;
    public string? DisplayName { get; } = displayName;
    public string Pin { get; } = pin;
}
