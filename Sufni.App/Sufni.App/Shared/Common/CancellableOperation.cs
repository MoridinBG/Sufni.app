using System;
using System.Threading;

namespace Sufni.App.Shared.Common;

public sealed class CancellableOperation : IDisposable
{
    private CancellationTokenSource? cts;

    /// Cancel any in-flight run, return a fresh token for the new one.
    public CancellationToken Start(CancellationToken cancellationToken = default)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
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
