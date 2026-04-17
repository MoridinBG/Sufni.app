using System;
using System.Net;
using System.Threading.Tasks;
using Sufni.App.Services;
using Sufni.App.Services.Management;
using Sufni.Telemetry;

namespace Sufni.App.Models;

public class NetworkTelemetryFile : ITelemetryFile
{
    public string Name { get; set; }
    public string FileName { get; }
    public bool? ShouldBeImported { get; set; }
    public bool Imported { get; set; }
    public string Description { get; set; }
    public byte Version { get; }
    public DateTime StartTime { get; init; }
    public string Duration { get; init; }
    public string? MalformedMessage => null;
    public bool HasUnknown => false;

    private readonly IPEndPoint ipEndPoint;
    private readonly IDaqManagementService daqManagementService;
    private readonly int recordId;

    public async Task<byte[]> GeneratePsstAsync(BikeData bikeData)
    {
        var rawFileResult = await daqManagementService.GetFileAsync(
            ipEndPoint.Address.ToString(),
            ipEndPoint.Port,
            DaqFileClass.RootSst,
            recordId);

        var loadedFile = rawFileResult switch
        {
            DaqGetFileResult.Loaded loaded => loaded,
            DaqGetFileResult.Error error => throw new DaqManagementException(error.Message),
            _ => throw new DaqManagementException("GET_FILE returned an unsupported result shape.")
        };

        var rawData = loadedFile.Bytes;
        var rawTelemetryData = RawTelemetryData.FromByteArray(rawData);
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

    public Task OnImported()
    {
        Imported = true;
        return Task.CompletedTask;
    }

    public async Task OnTrashed()
    {
        var result = await daqManagementService.TrashFileAsync(
            ipEndPoint.Address.ToString(),
            ipEndPoint.Port,
            recordId);

        if (result is DaqManagementResult.Error error)
        {
            throw new DaqManagementException(error.Message);
        }
    }

    public NetworkTelemetryFile(
        IPEndPoint source,
        IDaqManagementService daqManagementService,
        int recordId,
        string name,
        byte version,
        DateTimeOffset timestampUtc,
        TimeSpan duration)
    {
        this.daqManagementService = daqManagementService;
        this.recordId = recordId;
        ShouldBeImported = duration.TotalSeconds >= 5 ? true : null;
        Version = version;
        StartTime = timestampUtc.LocalDateTime;
        Duration = duration.ToString(@"hh\:mm\:ss");
        Name = name;
        FileName = name;
        Description = $"Imported from {name}";
        ipEndPoint = source;
    }
}