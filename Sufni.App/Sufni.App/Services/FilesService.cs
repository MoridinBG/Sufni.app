using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DynamicData.Kernel;
using Sufni.App.Services.Management;

namespace Sufni.App.Services;

public class FilesService(IBackgroundTaskRunner backgroundTaskRunner) : IFilesService
{
    private TopLevel? target;
    private readonly FilePickerFileType jsonType = new("JSON files")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
        AppleUniformTypeIdentifiers = ["public.json"]
    };
    private readonly FilePickerFileType csvType = new("CSV files")
    {
        Patterns = ["*.csv"],
        MimeTypes = ["text/csv", "text/plain"]
    };

    public void SetTarget(TopLevel? newTarget)
    {
        target = newTarget;
    }

    public Task OpenLogsFolderAsync()
    {
        AppPaths.CreateRequiredDirectories();

        if (Directory.Exists(AppPaths.LogsDirectory))
        {
            Process.Start(new ProcessStartInfo(AppPaths.LogsDirectory) { UseShellExecute = true });
        }

        return Task.CompletedTask;
    }

    public async Task<IStorageFolder?> OpenDataStoreFolderAsync()
    {
        Debug.Assert(target != null, nameof(target) + " != null");

        var folders = await target.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
        {
            Title = "Open SST data store",
            AllowMultiple = false,
        });

        return folders.Count == 1 ? folders[0] : null;
    }

    public async Task<IStorageFile?> OpenBikeImageFileAsync()
    {
        Debug.Assert(target != null, nameof(target) + " != null");

        var files = await target.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = "Open bike image file",
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
            AllowMultiple = false
        });

        return files.Count == 1 ? files[0] : null;
    }

    public async Task<IStorageFile?> SaveBikeFileAsync(string suggestedName)
    {
        Debug.Assert(target != null, nameof(target) + " != null");

        return await target.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
        {
            Title = "Save bike as JSON",
            SuggestedFileName = SanitizeFileName(suggestedName),
            DefaultExtension = ".json",
            FileTypeChoices = [jsonType],
        });
    }

    public async Task<IStorageFile?> OpenBikeFileAsync()
    {
        Debug.Assert(target != null, nameof(target) + " != null");

        var files = await target.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = "Open bike JSON",
            FileTypeFilter = [jsonType],
            AllowMultiple = false
        });

        return files.Count == 1 ? files[0] : null;
    }

    public async Task<IStorageFile?> SaveSetupFileAsync(string suggestedName)
    {
        Debug.Assert(target != null, nameof(target) + " != null");

        return await target.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
        {
            Title = "Save setup as JSON",
            SuggestedFileName = SanitizeFileName(suggestedName),
            DefaultExtension = ".json",
            FileTypeChoices = [jsonType],
        });
    }

    public async Task<IStorageFile?> OpenSetupFileAsync()
    {
        Debug.Assert(target != null, nameof(target) + " != null");

        var files = await target.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = "Open setup JSON",
            FileTypeFilter = [jsonType],
            AllowMultiple = false
        });

        return files.Count == 1 ? files[0] : null;
    }

    public async Task<IStorageFile?> OpenLeverageRatioCsvFileAsync()
    {
        Debug.Assert(target != null, nameof(target) + " != null");

        var files = await target.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = "Open leverage ratio CSV",
            FileTypeFilter = [csvType],
            AllowMultiple = false
        });

        return files.Count == 1 ? files[0] : null;
    }

    public async Task<List<IStorageFile>> OpenGpxFilesAsync()
    {
        Debug.Assert(target != null, nameof(target) + " != null");

        var files = await target.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open GPX file",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("GPX files")
                {
                    Patterns = ["*.gpx"],
                    MimeTypes = ["application/gpx+xml"]
                }
            ]
        });

        return files.AsList();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        return string.IsNullOrEmpty(sanitized) ? "untitled" : sanitized;
    }

    public async Task<SelectedDeviceConfigFile?> OpenDeviceConfigFileAsync(CancellationToken cancellationToken = default)
    {
        Debug.Assert(target != null, nameof(target) + " != null");

        var files = await target.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open device CONFIG",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.All]
        });

        if (files.Count != 1)
        {
            return null;
        }

        var file = files[0];
        return await backgroundTaskRunner.RunAsync(async () =>
        {
            await using var stream = await file.OpenReadAsync();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            return new SelectedDeviceConfigFile(file.Name, memoryStream.ToArray());
        }, cancellationToken);
    }
}