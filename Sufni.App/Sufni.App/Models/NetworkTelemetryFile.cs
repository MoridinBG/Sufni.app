using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Sufni.App.Services;
using Sufni.App.Services.Management;
using Sufni.Telemetry;

namespace Sufni.App.Models;

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

    public async Task<byte[]> GeneratePsstAsync(BikeData bikeData)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"sufni-{Guid.NewGuid():N}.SST");
        try
        {
            DaqGetFileResult downloadedFile;
            await using (var destination = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                downloadedFile = await daqManagementService.GetFileAsync(
                    ipEndPoint.Address.ToString(),
                    ipEndPoint.Port,
                    DaqFileClass.RootSst,
                    recordId,
                    destination);
            }

            var loadedFile = downloadedFile switch
            {
                DaqGetFileResult.Downloaded loaded => loaded,
                DaqGetFileResult.Error error => throw new DaqManagementException(error.ErrorCode, error.Message),
                _ => throw new DaqManagementException("GET_FILE returned an unsupported result shape.")
            };

            await using var source = new FileStream(
                tempPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var rawTelemetryData = RawTelemetryData.FromStream(source);
            var telemetryMetadata = new Metadata
            {
                SourceName = loadedFile.Name,
                Version = rawTelemetryData.Version,
                SampleRate = rawTelemetryData.SampleRate,
                Timestamp = rawTelemetryData.Timestamp,
                Duration = rawTelemetryData.SampleRate > 0
                    ? (double)Math.Max(rawTelemetryData.Front.Length, rawTelemetryData.Rear.Length) / rawTelemetryData.SampleRate
                    : 0.0
            };
            var telemetryData = TelemetryData.FromRecording(rawTelemetryData, telemetryMetadata, bikeData);
            return telemetryData.BinaryForm;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task OnImported()
    {
        var result = await daqManagementService.MarkSstUploadedAsync(
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
        var result = await daqManagementService.TrashFileAsync(
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
        ShouldBeImported = canImport
            ? effectiveDuration?.TotalSeconds >= 5 ? true : null
            : false;
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