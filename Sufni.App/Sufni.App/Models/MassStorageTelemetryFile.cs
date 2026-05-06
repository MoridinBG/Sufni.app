using System;
using System.IO;
using System.Threading;
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
    public byte Version { get; private set; }
    public DateTime StartTime { get; private set; }
    public string Duration { get; private set; } = "unknown";
    public string? MalformedMessage { get; private set; }
    public bool CanImport { get; private set; }
    public bool HasUnknown { get; private set; }

    public MassStorageTelemetryFile(FileInfo fileInfo)
    {
        this.fileInfo = fileInfo;

        using var stream = File.Open(this.fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        var inspection = RawTelemetryData.InspectStream(stream);
        ApplyInspection(inspection, fileInfo.LastWriteTime);
        Name = fileInfo.Name;
        Description = $"Imported from {fileInfo.Name}";
    }

    public async Task<TelemetryFileSource> ReadSourceAsync(CancellationToken cancellationToken = default)
    {
        var rawData = await File.ReadAllBytesAsync(fileInfo.FullName, cancellationToken);
        return new TelemetryFileSource(FileName, rawData);
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

    private void ApplyInspection(SstFileInspection inspection, DateTime fallbackStartTime)
    {
        switch (inspection)
        {
            case ValidSstFileInspection valid:
                ShouldBeImported = false;
                Version = valid.Version;
                StartTime = valid.StartTime;
                Duration = valid.Duration.ToString(@"hh\:mm\:ss");
                MalformedMessage = valid.MalformedMessage;
                CanImport = true;
                HasUnknown = valid.HasUnknown;
                break;
            case MalformedSstFileInspection malformed:
                ShouldBeImported = false;
                Version = malformed.Version ?? 0;
                StartTime = malformed.StartTime ?? fallbackStartTime;
                Duration = malformed.Duration?.ToString(@"hh\:mm\:ss") ?? "unknown";
                MalformedMessage = malformed.Message;
                CanImport = false;
                HasUnknown = false;
                break;
        }
    }
}
