using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Services.Management;

public interface IDaqManagementSession : IAsyncDisposable
{
    Task<DaqGetFileResult> GetFileAsync(
        DaqFileClass fileClass,
        int recordId,
        Stream destination,
        CancellationToken cancellationToken = default);

    Task<DaqManagementResult> MarkSstUploadedAsync(
        int recordId,
        CancellationToken cancellationToken = default);

    Task<DaqManagementResult> TrashFileAsync(
        int recordId,
        CancellationToken cancellationToken = default);
}
