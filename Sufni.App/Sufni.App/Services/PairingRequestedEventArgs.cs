using System;

namespace Sufni.App.Services;

public sealed class PairingRequestedEventArgs(string deviceId, string pin) : EventArgs
{
    public string DeviceId { get; } = deviceId;
    public string Pin { get; } = pin;
}
