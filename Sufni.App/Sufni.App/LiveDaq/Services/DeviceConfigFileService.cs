using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;

namespace Sufni.App.LiveDaq.Services;

/// <summary>Reads a selected device configuration outside the UI thread.</summary>
public sealed class DeviceConfigFileService(IFilesService files, IBackgroundTaskRunner background)
{
    public async Task<SelectedDeviceConfigFile?> SelectAsync(CancellationToken cancellationToken)
    {
        var file = await files.OpenDeviceConfigFileAsync(cancellationToken);
        if (file is null) return null;
        return await background.RunAsync(async () =>
        {
            await using var stream = await file.OpenReadAsync();
            using var bytes = new MemoryStream();
            await stream.CopyToAsync(bytes, cancellationToken);
            return new SelectedDeviceConfigFile(file.Name, bytes.ToArray());
        }, cancellationToken);
    }
}
