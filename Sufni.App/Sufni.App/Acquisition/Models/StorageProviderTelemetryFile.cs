using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace Sufni.App.Acquisition.Models;

public class StorageProviderTelemetryFile : ITelemetryFile
{
    private readonly IStorageFile storageFile;

    public string FileName => storageFile.Name;
    public DateTime StartTime { get; private set; }
    public string Duration { get; private set; }
    public string? MalformedMessage { get; private set; }
    public bool CanImport { get; private set; }

    private async Task Init()
    {
        await using var stream = await storageFile.OpenReadAsync();
        var inspection = TelemetryFileInspector.Inspect(stream, FileName);
        ApplyInspection(TelemetryFileInspectionMapper.Map(inspection, ResolveFallbackStartTime(storageFile)));
    }

    private StorageProviderTelemetryFile(IStorageFile storageFile)
    {
        this.storageFile = storageFile;
        StartTime = ResolveFallbackStartTime(storageFile);
        Duration = "unknown";
    }

    public static async Task<StorageProviderTelemetryFile> CreateAsync(IStorageFile storageFile)
    {
        var telemetryFile = new StorageProviderTelemetryFile(storageFile);
        await telemetryFile.Init();
        return telemetryFile;
    }

    public async Task<TelemetryFileSource> ReadSourceAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = await storageFile.OpenReadAsync();
        using var memory = new MemoryStream(GetInitialCapacity(stream));
        await stream.CopyToAsync(memory, cancellationToken);
        return TelemetryFileSource.TakeOwnership(FileName, memory);
    }

    public async Task OnImported()
    {
        await MoveToSiblingFolderAsync(DaqDriveLayout.UploadedDirectoryName, "The \"uploaded\" folder could not be accessed.");
    }

    public async Task OnTrashed()
    {
        await MoveToSiblingFolderAsync(DaqDriveLayout.TrashDirectoryName, "The \"trash\" folder could not be accessed.");
    }

    private async Task MoveToSiblingFolderAsync(string folderName, string missingMessage)
    {
        var parent = await storageFile.GetParentAsync();
        var parentItems = parent!.GetItemsAsync();
        IStorageFolder? target = null;
        await foreach (var item in parentItems)
        {
            if (!item.Name.Equals(folderName, StringComparison.Ordinal)) continue;
            target = item as IStorageFolder;
            break;
        }

        if (target is null)
        {
            throw new Exception(missingMessage);
        }

        await storageFile.MoveAsync(target);
    }

    private void ApplyInspection(TelemetryFileInspectionState state)
    {
        StartTime = state.StartTime;
        Duration = state.Duration;
        MalformedMessage = state.MalformedMessage;
        CanImport = state.CanImport;
    }

    private static DateTime ResolveFallbackStartTime(IStorageFile storageFile)
    {
        var localPath = storageFile.TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
        {
            return new FileInfo(localPath).LastWriteTimeUtc;
        }

        return DateTimeOffset.UnixEpoch.UtcDateTime;
    }

    private static int GetInitialCapacity(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return 0;
        }

        var remainingLength = stream.Length - stream.Position;
        return remainingLength is > 0 and <= int.MaxValue ? (int)remainingLength : 0;
    }
}
