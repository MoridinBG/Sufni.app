using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Services.Management;

internal sealed class DaqManagementSession : IDaqManagementSession
{
    private readonly ManagementClient client;
    private bool disposed;

    internal DaqManagementSession(ManagementClient client)
    {
        this.client = client;
    }

    public Task<DaqGetFileResult> GetFileAsync(
        DaqFileClass fileClass,
        int recordId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return client.GetFileAsync(fileClass, recordId, destination, cancellationToken);
    }

    public Task<DaqManagementResult> MarkSstUploadedAsync(
        int recordId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return client.MarkSstUploadedAsync(recordId, cancellationToken);
    }

    public Task<DaqManagementResult> TrashFileAsync(
        int recordId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return client.TrashFileAsync(recordId, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        disposed = true;
        client.Dispose();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(DaqManagementSession));
        }
    }
}
