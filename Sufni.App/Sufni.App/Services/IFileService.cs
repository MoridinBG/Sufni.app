using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Sufni.App.Services;

public interface IFilesService
{
    public void SetTarget(TopLevel? target);
    public Task OpenLogsFolderAsync();
    public Task<IStorageFolder?> OpenDataStoreFolderAsync();
    public Task<IStorageFile?> OpenBikeImageFileAsync();
    public Task<IStorageFile?> SaveBikeFileAsync();
    public Task<IStorageFile?> OpenBikeFileAsync();
    public Task<List<IStorageFile>> OpenGpxFilesAsync();
}