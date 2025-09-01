using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sufni.Telemetry;

namespace Sufni.App.Models;

public class MassStorageTelemetryFile : ITelemetryFile
{
    private readonly FileInfo fileInfo;

    public string Name { get; set; }
    public string FileName => fileInfo.Name;
    public bool? ShouldBeImported { get; set; }
    public bool Imported { get; set; }
    public string Description { get; set; }
    public DateTime StartTime { get; init; }
    public string Duration { get; init; }

    public MassStorageTelemetryFile(FileInfo fileInfo)
    {
        this.fileInfo = fileInfo;

        using var stream = File.Open(this.fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);

        var magic = reader.ReadBytes(3);
        var version = reader.ReadByte();
        if (!magic.SequenceEqual("SST"u8.ToArray()) || version != 3)
        {
            throw new FormatException("Not an SST file");
        }

        var sampleRate = reader.ReadUInt16();
        var count = (this.fileInfo.Length - 16 /* sizeof(header) */) / 4 /* sizeof(record) */;
        reader.ReadUInt16(); // padding
        var timestamp = reader.ReadInt64();

        var duration = TimeSpan.FromSeconds((double)count / sampleRate);
        ShouldBeImported = duration.TotalSeconds >= 5 ? true : null;
        StartTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
        Duration = duration.ToString(@"hh\:mm\:ss");
        Name = fileInfo.Name;
        Description = $"Imported from {fileInfo.Name}";
    }

    public async Task<byte[]> GeneratePsstAsync(BikeData bikeData)
    {
        var rawData = await File.ReadAllBytesAsync(fileInfo.FullName);
        var rawTelemetryData = RawTelemetryData.FromByteArray(rawData);
        var telemetryMetadata = new Metadata
        {
            SourceName = FileName,
            Version = rawTelemetryData.Version,
            SampleRate = rawTelemetryData.SampleRate,
            Timestamp = rawTelemetryData.Timestamp,
            Duration = (double)rawTelemetryData.Front.Length / rawTelemetryData.SampleRate
        };
        var telemetryData = TelemetryData.FromRecording(rawTelemetryData.Front, rawTelemetryData.Rear,
            rawTelemetryData.FrontAnomalyRate, rawTelemetryData.RearAnomalyRate, telemetryMetadata, bikeData);
        return telemetryData.BinaryForm;
    }

    public Task OnImported()
    {
        Imported = true;
        File.Move(fileInfo.FullName,
            $"{Path.GetDirectoryName(fileInfo.FullName)}/uploaded/{fileInfo.Name}");
        return Task.CompletedTask;
    }

    public Task OnTrashed()
    {
        File.Move(fileInfo.FullName,
            $"{Path.GetDirectoryName(fileInfo.FullName)}/trash/{fileInfo.Name}");
        return Task.CompletedTask;
    }
}