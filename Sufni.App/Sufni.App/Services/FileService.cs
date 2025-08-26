using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DynamicData.Kernel;

namespace Sufni.App.Services;

public class FilesService : IFilesService
{
    private TopLevel? target;
    private readonly FilePickerFileType jsonType = new("JSON files")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"]
    };

    public void SetTarget(TopLevel? newTarget)
    {
        target = newTarget;
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

    public async Task<IStorageFile?> SaveBikeFileAsync()
    {
        Debug.Assert(target != null, nameof(target) + " != null");

        return await target.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
        {
            Title = "Save bike as JSON",
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
}