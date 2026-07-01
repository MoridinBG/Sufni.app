using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.Extensibility.RecordedSessions;

internal sealed class RecordedSessionOperationCoordinator : IAsyncDisposable
{
    private readonly System.Threading.Lock gate = new();
    private readonly Action<string, double> reportOperation;
    private readonly Action completeOperation;
    private long currentOperationId;
    private CancellationTokenSource? currentCancellation;

    public RecordedSessionOperationCoordinator(
        Action<string, double> reportOperation,
        Action completeOperation)
    {
        ArgumentNullException.ThrowIfNull(reportOperation);
        ArgumentNullException.ThrowIfNull(completeOperation);

        this.reportOperation = reportOperation;
        this.completeOperation = completeOperation;
    }

    public IRecordedSessionOperationLease StartOperation(string message)
    {
        CancellationTokenSource cancellation;
        long operationId;
        lock (gate)
        {
            currentCancellation?.Cancel();
            currentCancellation?.Dispose();
            currentCancellation = new CancellationTokenSource();
            cancellation = currentCancellation;
            operationId = ++currentOperationId;
        }

        var lease = new Lease(this, operationId, cancellation);
        lease.Report(message, 0);
        return lease;
    }

    public void CancelCurrent()
    {
        CancellationTokenSource? cancellation;
        lock (gate)
        {
            cancellation = currentCancellation;
            currentCancellation = null;
            currentOperationId++;
        }

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
        completeOperation();
    }

    public ValueTask DisposeAsync()
    {
        CancelCurrent();
        return ValueTask.CompletedTask;
    }

    private bool IsCurrent(long operationId, CancellationTokenSource cancellation)
    {
        lock (gate)
        {
            return ReferenceEquals(currentCancellation, cancellation) &&
                   currentOperationId == operationId;
        }
    }

    private void Report(long operationId, CancellationTokenSource cancellation, string message, double percent)
    {
        if (!IsCurrent(operationId, cancellation))
        {
            return;
        }

        reportOperation(message, percent);
    }

    private void Complete(long operationId, CancellationTokenSource cancellation)
    {
        if (!IsCurrent(operationId, cancellation))
        {
            return;
        }

        lock (gate)
        {
            if (!ReferenceEquals(currentCancellation, cancellation) ||
                currentOperationId != operationId)
            {
                return;
            }

            currentCancellation = null;
            currentOperationId++;
        }

        cancellation.Dispose();
        completeOperation();
    }

    private sealed class Lease : IRecordedSessionOperationLease
    {
        private readonly RecordedSessionOperationCoordinator owner;
        private readonly long operationId;
        private readonly CancellationToken token;
        private CancellationTokenSource? cancellation;

        public Lease(
            RecordedSessionOperationCoordinator owner,
            long operationId,
            CancellationTokenSource cancellation)
        {
            this.owner = owner;
            this.operationId = operationId;
            this.cancellation = cancellation;
            token = cancellation.Token;
        }

        public CancellationToken CancellationToken =>
            token;

        public bool IsCurrent =>
            cancellation is { } activeCancellation &&
            owner.IsCurrent(operationId, activeCancellation);

        public void Report(string message, double percent)
        {
            if (cancellation is not { } activeCancellation)
            {
                return;
            }

            owner.Report(operationId, activeCancellation, message, percent);
        }

        public void Complete()
        {
            if (cancellation is not { } activeCancellation)
            {
                return;
            }

            cancellation = null;
            owner.Complete(operationId, activeCancellation);
        }

        public ValueTask DisposeAsync()
        {
            Complete();
            return ValueTask.CompletedTask;
        }
    }
}
