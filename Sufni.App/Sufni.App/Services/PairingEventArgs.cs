using System;
using Sufni.App.Models;

namespace Sufni.App.Services;

public sealed class PairingEventArgs(PairedDevice device) : EventArgs
{
    public PairedDevice Device { get; set; } = device;
}