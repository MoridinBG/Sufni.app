using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Acquisition.Services.Management;

namespace Sufni.App.Acquisition.Models;

public class NetworkTelemetryFile : ITelemetryFile
{
    private static readonly DateTimeOffset FallbackStartTimeUtc = DateTimeOffset.UnixEpoch;

    public string FileName { get; }
    public DateTime StartTime { get; init; }
    public string Duration { get; init; }
    public string? MalformedMessage { get; }
    public bool CanImport { get; }

    private readonly Func<CancellationToken, Task<IDaqManagementSession>> getSession;
    private readonly int recordId;

    public async Task<TelemetryFileSource> ReadSourceAsync(CancellationToken cancellationToken = default)
    {
        var activeSession = await getSession(cancellationToken);
        using var destination = new MemoryStream();
        var downloadedFile = await activeSession.GetFileAsync(
            DaqFileClass.RootSst,
            recordId,
            destination,
            cancellationToken);

        var loadedFile = downloadedFile switch
        {
            DaqGetFileResult.Downloaded loaded => loaded,
            DaqGetFileResult.Error error => throw new DaqManagementException(error.ErrorCode, error.Message),
            _ => throw new DaqManagementException("GET_FILE returned an unsupported result shape.")
        };

        var sourceName = string.IsNullOrWhiteSpace(loadedFile.Name) ? FileName : loadedFile.Name;
        return TelemetryFileSource.TakeOwnership(sourceName, destination);
    }

    public async Task OnImported()
    {
        var activeSession = await getSession(CancellationToken.None);
        var result = await activeSession.MarkSstUploadedAsync(recordId);

        if (result is DaqManagementResult.Error error)
        {
            throw new DaqManagementException(error.ErrorCode, error.Message);
        }

    }

    public async Task OnTrashed()
    {
        var activeSession = await getSession(CancellationToken.None);
        var result = await activeSession.TrashFileAsync(recordId);

        if (result is DaqManagementResult.Error error)
        {
            throw new DaqManagementException(error.ErrorCode, error.Message);
        }
    }

    public NetworkTelemetryFile(
        Func<CancellationToken, Task<IDaqManagementSession>> getSession,
        int recordId,
        string name,
        DateTimeOffset? timestampUtc,
        TimeSpan? duration,
        string? malformedMessage = null,
        bool canImport = true)
    {
        this.recordId = recordId;

        var effectiveDuration = duration;
        CanImport = canImport;
        StartTime = (timestampUtc ?? FallbackStartTimeUtc).UtcDateTime;
        Duration = effectiveDuration?.ToString(@"hh\:mm\:ss") ?? "unknown";
        MalformedMessage = malformedMessage;
        FileName = name;
        this.getSession = getSession;
    }
}
