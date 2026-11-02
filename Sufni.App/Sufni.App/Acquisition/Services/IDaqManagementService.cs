using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Infrastructure;

namespace Sufni.App.Acquisition.Services;

public interface IDaqManagementService
{
    Task<DaqListDirectoryResult> ListDirectoryAsync(
        ServiceEndpoint endpoint,
        CancellationToken cancellationToken = default);

    Task<DaqConfigReadResult> ReadConfigAsync(
        ServiceEndpoint endpoint,
        CancellationToken cancellationToken = default);

    Task<DaqManagementResult> MarkSstUploadedAsync(
        ServiceEndpoint endpoint,
        int recordId,
        CancellationToken cancellationToken = default);

    Task<DaqManagementResult> TrashFileAsync(
        ServiceEndpoint endpoint,
        int recordId,
        CancellationToken cancellationToken = default);

    Task<DaqSetTimeResult> SetTimeAsync(
        ServiceEndpoint endpoint,
        CancellationToken cancellationToken = default);

    Task<DaqManagementResult> WriteConfigAsync(
        ServiceEndpoint endpoint,
        byte[] configBytes,
        CancellationToken cancellationToken = default);

    Task<IDaqManagementSession> OpenSessionAsync(
        ServiceEndpoint endpoint,
        CancellationToken cancellationToken = default);
}
