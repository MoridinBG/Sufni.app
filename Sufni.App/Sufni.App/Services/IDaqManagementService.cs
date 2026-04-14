using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Services.Management;

namespace Sufni.App.Services;

public interface IDaqManagementService
{
    Task<DaqListDirectoryResult> ListDirectoryAsync(
        string host,
        int port,
        DaqDirectoryId directoryId,
        CancellationToken cancellationToken = default);

    Task<DaqGetFileResult> GetFileAsync(
        string host,
        int port,
        DaqFileClass fileClass,
        int recordId,
        CancellationToken cancellationToken = default);

    Task<DaqManagementResult> TrashFileAsync(
        string host,
        int port,
        int recordId,
        CancellationToken cancellationToken = default);

    Task<DaqSetTimeResult> SetTimeAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default);

    Task<DaqManagementResult> ReplaceConfigAsync(
        string host,
        int port,
        byte[] configBytes,
        CancellationToken cancellationToken = default);
}