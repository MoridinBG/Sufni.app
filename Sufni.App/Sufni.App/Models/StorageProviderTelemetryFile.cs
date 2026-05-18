using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Sufni.Telemetry;

namespace Sufni.App.Models;

public class StorageProviderTelemetryFile : ITelemetryFile
{
    private readonly IStorageFile storageFile;

    public string Name { get; set; }
    public string FileName => storageFile.Name;
    public bool? ShouldBeImported { get; set; }
    public bool Imported { get; set; }
    public string Description { get; set; }
    public byte Version { get; private set; }
    public DateTime StartTime { get; private set; }
    public string Duration { get; private set; }
    public string? MalformedMessage { get; private set; }
    public bool CanImport { get; private set; }
    public bool HasUnknown { get; private set; }

    private async Task Init()
    {
        await using var stream = await storageFile.OpenReadAsync();
        var inspection = RawTelemetryData.InspectStream(stream);
        ApplyInspection(TelemetryFileInspectionMapper.Map(inspection, ResolveFallbackStartTime(storageFile)));
    }

    private StorageProviderTelemetryFile(IStorageFile storageFile)
    {
        this.storageFile = storageFile;
        Name = storageFile.Name;
        Description = $"Imported from {storageFile.Name}";
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
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return new TelemetryFileSource(FileName, memory.ToArray());
    }

    public async Task OnImported()
    {
        Imported = true;
        await MoveToSiblingFolderAsync("uploaded", "The \"uploaded\" folder could not be accessed.");
    }

    public async Task OnTrashed()
    {
        await MoveToSiblingFolderAsync("trash", "The \"trash\" folder could not be accessed.");
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
        ShouldBeImported = state.ShouldBeImported;
        Version = state.Version;
        StartTime = state.StartTime;
        Duration = state.Duration;
        MalformedMessage = state.MalformedMessage;
        CanImport = state.CanImport;
        HasUnknown = state.HasUnknown;
    }

    private static DateTime ResolveFallbackStartTime(IStorageFile storageFile)
    {
        var localPath = storageFile.TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
        {
            return new FileInfo(localPath).LastWriteTime;
        }

        return DateTimeOffset.UnixEpoch.LocalDateTime;
    }
}
