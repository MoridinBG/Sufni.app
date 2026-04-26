using System;
using System.IO;
using System.Linq;
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
        ApplyInspection(inspection);
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

    public async Task<byte[]> GeneratePsstAsync(BikeData bikeData)
    {
        await using var stream = await storageFile.OpenReadAsync();
        var rawTelemetryData = RawTelemetryData.FromStream(stream);
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

    public async Task OnImported()
    {
        Imported = true;
        var parent = await storageFile.GetParentAsync();
        var parentItems = parent!.GetItemsAsync();
        IStorageFolder? uploaded = null;
        await foreach (var item in parentItems)
        {
            if (!item.Name.Equals("uploaded")) continue;
            uploaded = item as IStorageFolder;
            break;
        }

        if (uploaded is null)
        {
            throw new Exception("The \"uploaded\" folder could not be accessed.");
        }

        await storageFile.MoveAsync(uploaded);
    }

    public async Task OnTrashed()
    {
        var parent = await storageFile.GetParentAsync();
        var parentItems = parent!.GetItemsAsync();
        IStorageFolder? trash = null;
        await foreach (var item in parentItems)
        {
            if (!item.Name.Equals("trash")) continue;
            trash = item as IStorageFolder;
            break;
        }

        if (trash is null)
        {
            throw new Exception("The \"trash\" folder could not be accessed.");
        }

        await storageFile.MoveAsync(trash);
    }

    private void ApplyInspection(SstFileInspection inspection)
    {
        switch (inspection)
        {
            case ValidSstFileInspection valid:
                ShouldBeImported = valid.Duration.TotalSeconds >= 5 ? true : null;
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
                StartTime = malformed.StartTime ?? ResolveFallbackStartTime(storageFile);
                Duration = malformed.Duration?.ToString(@"hh\:mm\:ss") ?? "unknown";
                MalformedMessage = malformed.Message;
                CanImport = false;
                break;
        }
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