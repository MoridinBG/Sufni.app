using System;
using Sufni.App.Models;

namespace Sufni.App.Services;

public sealed class SynchronizationDataArrivedEventArgs(SynchronizationData data) : EventArgs
{
    public SynchronizationData Data { get; } = data;
}
