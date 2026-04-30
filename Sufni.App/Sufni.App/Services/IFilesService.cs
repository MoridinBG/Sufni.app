using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Sufni.App.Services.Management;

namespace Sufni.App.Services;

public interface IFilesService
{
    public void SetTarget(TopLevel? target);
    public Task OpenLogsFolderAsync();
    public Task<IStorageFolder?> OpenDataStoreFolderAsync();
    public Task<IStorageFile?> OpenBikeImageFileAsync();
    public Task<IStorageFile?> SaveBikeFileAsync(string suggestedName);
    public Task<IStorageFile?> OpenBikeFileAsync();
    public Task<IStorageFile?> SaveSetupFileAsync(string suggestedName);
    public Task<IStorageFile?> OpenSetupFileAsync();
    public Task<IStorageFile?> OpenLeverageRatioCsvFileAsync();
    public Task<List<IStorageFile>> OpenGpxFilesAsync();
    public Task<SelectedDeviceConfigFile?> OpenDeviceConfigFileAsync(CancellationToken cancellationToken = default);
}