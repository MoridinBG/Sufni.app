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
    public byte Version { get; }
    public DateTime StartTime { get; init; }
    public string Duration { get; init; }
    public string? MalformedMessage => null;
    public bool HasUnknown => false;

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
        var idString = FileName[..5].TrimStart('0');
        var idInt = int.Parse(idString);
        await SstTcpClient.TrashFile(ipEndPoint, idInt);
    }

    public NetworkTelemetryFile(IPEndPoint source, string name, byte version, ulong timestamp, TimeSpan duration)
    {
        ShouldBeImported = duration.TotalSeconds >= 5 ? true : null;
        Version = version;
        StartTime = DateTimeOffset.FromUnixTimeSeconds((int)timestamp).LocalDateTime;
        Duration = duration.ToString(@"hh\:mm\:ss");
        Name = name;
        FileName = name;
        Description = $"Imported from {name}";
        ipEndPoint = source;
    }
}