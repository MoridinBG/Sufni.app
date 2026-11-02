using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Infrastructure;

namespace Sufni.App.Acquisition.Models;

public class NetworkTelemetryFile : ITelemetryFile
{
    private static readonly DateTimeOffset FallbackStartTimeUtc = DateTimeOffset.UnixEpoch;

    public string Name { get; set; }
    public string FileName { get; }
    public ImportFileAction Action { get; set; }
    public string Description { get; set; }
    public DateTime StartTime { get; init; }
    public string Duration { get; init; }
    public string? MalformedMessage { get; }
    public bool CanImport { get; }

    private readonly ServiceEndpoint ipEndPoint;
    private readonly int recordId;
    private IDaqManagementSession activeSession = null!;

    public ServiceEndpoint EndPoint => ipEndPoint;

    public void AttachSession(IDaqManagementSession session)
    {
        activeSession = session;
    }

    public async Task<TelemetryFileSource> ReadSourceAsync(CancellationToken cancellationToken = default)
    {
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
        var result = await activeSession.MarkSstUploadedAsync(recordId);

        if (result is DaqManagementResult.Error error)
        {
            throw new DaqManagementException(error.ErrorCode, error.Message);
        }

    }

    public async Task OnTrashed()
    {
        var result = await activeSession.TrashFileAsync(recordId);

        if (result is DaqManagementResult.Error error)
        {
            throw new DaqManagementException(error.ErrorCode, error.Message);
        }
    }

    public NetworkTelemetryFile(
        ServiceEndpoint source,
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
        Name = name;
        FileName = name;
        Description = $"Imported from {name}";
        ipEndPoint = source;
    }
}
