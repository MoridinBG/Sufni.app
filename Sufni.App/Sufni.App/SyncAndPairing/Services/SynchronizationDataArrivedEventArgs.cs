using System;

using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.SyncAndPairing.Services;

public sealed class SynchronizationDataArrivedEventArgs(SynchronizationData data) : EventArgs
{
    public SynchronizationData Data { get; } = data;
}
