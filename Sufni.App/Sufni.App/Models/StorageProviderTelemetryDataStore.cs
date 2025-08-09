using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace Sufni.App.Models;

public class StorageProviderTelemetryDataStore : ITelemetryDataStore
{
    public Task Initialization { get; }
    public string Name { get; }
    public Guid? BoardId { get; private set; }
    private IStorageFolder Folder { get; }

    public bool IsAvailable()
    {
        try
        {
            Folder.GetItemsAsync();
        }
        catch
        {
            return false;
        }

        return true;
    }

    public async Task<List<ITelemetryFile>> GetFiles()
    {
        await Initialization;

        var files = new List<ITelemetryFile>();
        var items = Folder.GetItemsAsync();
        await foreach (var item in items)
        {
            if (item.Name.EndsWith(".SST") && item is IStorageFile file)
            {
                files.Add(new StorageProviderTelemetryFile(file));
            }
        }

        return files.OrderByDescending(f => f.StartTime).ToList();
    }

    private async Task Init()
    {
        IStorageFile? boardIdFile = null;
        IStorageFolder? uploadedFolder = null;
        var items = Folder.GetItemsAsync();
        await foreach (var item in items)
        {
            if (item.Name.Equals("BOARDID") && item is IStorageFile file)
            {
                boardIdFile = file;
                if (uploadedFolder is not null) break;
            }

            if (item.Name.Equals("uploaded") && item is IStorageFolder folder)
            {
                uploadedFolder = folder;
                if (boardIdFile is not null) break;
            }
        }

        if (uploadedFolder is null)
            await Folder.CreateFolderAsync("uploaded");

        if (boardIdFile is null) return;

        await using var stream = await boardIdFile.OpenReadAsync();
        var buffer = new byte[16];
        await stream.ReadExactlyAsync(buffer, 0, 16);
        var serialHex = Encoding.ASCII.GetString(buffer).ToLower();
        BoardId = UuidUtil.CreateDeviceUuid(serialHex);
    }

    public StorageProviderTelemetryDataStore(IStorageFolder folder)
    {
        Folder = folder;
        Name = folder.Name;
        Initialization = Init();
    }
}