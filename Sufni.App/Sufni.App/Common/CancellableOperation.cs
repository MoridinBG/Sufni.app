using System;
using System.Threading;

namespace Sufni.App;

public sealed class CancellableOperation : IDisposable
{
    private CancellationTokenSource? cts;

    /// Cancel any in-flight run, return a fresh token for the new one.
    public CancellationToken Start()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = new CancellationTokenSource();
        return cts.Token;
    }

    public void Cancel()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    public void Dispose() => Cancel();
}
