using System;

using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.SyncAndPairing.Services;

public sealed class PairingEventArgs(PairedDevice device) : EventArgs
{
    public PairedDevice Device { get; set; } = device;
}