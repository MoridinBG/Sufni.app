using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.Services.Management;
namespace Sufni.App.Acquisition.Models;

public class NetworkTelemetryFile : ITelemetryFile
{
    private static readonly DateTimeOffset FallbackStartTimeUtc = DateTimeOffset.UnixEpoch;

    public string Name { get; set; }
    public string FileName { get; }
    public bool? ShouldBeImported { get; set; }
    public bool Imported { get; set; }
    public string Description { get; set; }
    public byte Version { get; }
    public DateTime StartTime { get; init; }
    public string Duration { get; init; }
    public string? MalformedMessage { get; }
    public bool CanImport { get; }
    public bool HasUnknown => false;

    private readonly IPEndPoint ipEndPoint;
    private readonly IDaqManagementService daqManagementService;
    private readonly int recordId;
    private IDaqManagementSession? activeSession;

    public IPEndPoint EndPoint => ipEndPoint;

    public void AttachSession(IDaqManagementSession? session)
    {
        activeSession = session;
    }

    public async Task<TelemetryFileSource> ReadSourceAsync(CancellationToken cancellationToken = default)
    {
        using var destination = new MemoryStream();
        var downloadedFile = activeSession is not null
            ? await activeSession.GetFileAsync(
                DaqFileClass.RootSst,
                recordId,
                destination,
                cancellationToken)
            : await daqManagementService.GetFileAsync(
                ipEndPoint.Address.ToString(),
                ipEndPoint.Port,
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
        return new TelemetryFileSource(sourceName, destination.ToArray());
    }

    public async Task OnImported()
    {
        var result = activeSession is not null
            ? await activeSession.MarkSstUploadedAsync(recordId)
            : await daqManagementService.MarkSstUploadedAsync(
                ipEndPoint.Address.ToString(),
                ipEndPoint.Port,
                recordId);

        if (result is DaqManagementResult.Error error)
        {
            throw new DaqManagementException(error.ErrorCode, error.Message);
        }

        Imported = true;
    }

    public async Task OnTrashed()
    {
        var result = activeSession is not null
            ? await activeSession.TrashFileAsync(recordId)
            : await daqManagementService.TrashFileAsync(
                ipEndPoint.Address.ToString(),
                ipEndPoint.Port,
                recordId);

        if (result is DaqManagementResult.Error error)
        {
            throw new DaqManagementException(error.ErrorCode, error.Message);
        }
    }

    public NetworkTelemetryFile(
        IPEndPoint source,
        IDaqManagementService daqManagementService,
        int recordId,
        string name,
        byte version,
        DateTimeOffset? timestampUtc,
        TimeSpan? duration,
        string? malformedMessage = null,
        bool canImport = true)
    {
        this.daqManagementService = daqManagementService;
        this.recordId = recordId;

        var effectiveDuration = duration;
        CanImport = canImport;
        ShouldBeImported = false;
        Version = version;
        StartTime = (timestampUtc ?? FallbackStartTimeUtc).LocalDateTime;
        Duration = effectiveDuration?.ToString(@"hh\:mm\:ss") ?? "unknown";
        MalformedMessage = malformedMessage;
        Name = name;
        FileName = name;
        Description = $"Imported from {name}";
        ipEndPoint = source;
    }
}
