using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Acquisition.Models;

public class MassStorageTelemetryFile : ITelemetryFile
{
    private readonly FileInfo fileInfo;

    public string FileName => fileInfo.Name;
    public DateTime StartTime { get; private set; }
    public string Duration { get; private set; } = "unknown";
    public string? MalformedMessage { get; private set; }
    public bool CanImport { get; private set; }

    public MassStorageTelemetryFile(FileInfo fileInfo)
    {
        this.fileInfo = fileInfo;

        using var stream = File.Open(this.fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        var inspection = TelemetryFileInspector.Inspect(stream, FileName);
        ApplyInspection(TelemetryFileInspectionMapper.Map(inspection, fileInfo.LastWriteTimeUtc));
    }

    public async Task<TelemetryFileSource> ReadSourceAsync(CancellationToken cancellationToken = default)
    {
        var rawData = await File.ReadAllBytesAsync(fileInfo.FullName, cancellationToken);
        return new TelemetryFileSource(FileName, rawData);
    }

    public Task OnImported()
    {
        File.Move(fileInfo.FullName,
            Path.Combine(Path.GetDirectoryName(fileInfo.FullName)!, DaqDriveLayout.UploadedDirectoryName, fileInfo.Name));
        return Task.CompletedTask;
    }

    public Task OnTrashed()
    {
        File.Move(fileInfo.FullName,
            Path.Combine(Path.GetDirectoryName(fileInfo.FullName)!, DaqDriveLayout.TrashDirectoryName, fileInfo.Name));
        return Task.CompletedTask;
    }

    private void ApplyInspection(TelemetryFileInspectionState state)
    {
        StartTime = state.StartTime;
        Duration = state.Duration;
        MalformedMessage = state.MalformedMessage;
        CanImport = state.CanImport;
    }
}
