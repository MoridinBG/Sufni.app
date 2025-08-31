using System;
using System.Net;
using System.Threading.Tasks;
using Sufni.Telemetry;

namespace Sufni.App.Models;

public class NetworkTelemetryFile : ITelemetryFile
{
    public string Name { get; set; }
    public string FileName { get; }
    public bool? ShouldBeImported { get; set; }
    public bool Imported { get; set; }
    public string Description { get; set; }
    public DateTime StartTime { get; init; }
    public string Duration { get; init; }

    private readonly IPEndPoint ipEndPoint;

    public async Task<byte[]> GeneratePsstAsync(BikeData bikeData)
    {
        var idString = FileName[..5].TrimStart('0');
        var idInt = int.Parse(idString);
        var rawData = await SstTcpClient.GetFile(ipEndPoint, idInt);
        var rawTelemetryData = RawTelemetryData.FromByteArray(rawData);
        var telemetryMetadata = new Metadata
        {
            SourceName = FileName,
            Version = rawTelemetryData.Version,
            SampleRate = rawTelemetryData.SampleRate,
            Timestamp = rawTelemetryData.Timestamp
        };
        var telemetryData = TelemetryData.FromRecording(rawTelemetryData.Front, rawTelemetryData.Rear,
            rawTelemetryData.FrontAnomalyRate, rawTelemetryData.RearAnomalyRate, telemetryMetadata, bikeData);
        return telemetryData.BinaryForm;
    }

    public Task OnImported()
    {
        Imported = true;
        return Task.CompletedTask;
    }

    public async Task OnTrashed()
    {
        var idString = FileName[..5].TrimStart('0');
        var idInt = int.Parse(idString);
        await SstTcpClient.TrashFile(ipEndPoint, idInt);
    }

    public NetworkTelemetryFile(IPEndPoint source, ushort sampleRate, string name, ulong size, ulong timestamp)
    {
        var count = (size - 16 /* sizeof(header) */) / 4 /* sizeof(record) */;
        var duration = TimeSpan.FromSeconds((double)count / sampleRate);
        ShouldBeImported = duration.TotalSeconds >= 5 ? true : null;
        StartTime = DateTimeOffset.FromUnixTimeSeconds((int)timestamp).LocalDateTime;
        Duration = duration.ToString(@"hh\:mm\:ss");
        Name = name;
        FileName = name;
        Description = $"Imported from {name}";
        ipEndPoint = source;
    }
}