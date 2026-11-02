using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Setups.Models;
using Sufni.App.Shared.Models;

namespace Sufni.App.Setups.Services;

public sealed class SetupFileService(IFilesService files, IBackgroundTaskRunner background) : ISetupFileService
{
    private static readonly ILogger logger = Log.ForContext<SetupFileService>();

    public async Task<SetupFileReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var file = await files.OpenSetupFileAsync();
            if (file is null) return new SetupFileReadResult.Canceled();
            var payload = await background.RunAsync(async () =>
            {
                await using var stream = await file.OpenReadAsync();
                using var reader = new StreamReader(stream);
                return SetupFileFormat.Parse(await reader.ReadToEndAsync(cancellationToken));
            }, cancellationToken);
            if (payload is not null) return new SetupFileReadResult.Read(payload);
            logger.Warning("Setup import rejected because the selected file contained no setup");
            return new SetupFileReadResult.InvalidFile("JSON file was not a valid setup file.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or Sufni.Kinematics.LeverageRatioValidationException)
        {
            logger.Warning(exception, "Setup import rejected because the selected file was invalid");
            return new SetupFileReadResult.InvalidFile("JSON file was not a valid setup file.");
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Setup import failed while reading file");
            return new SetupFileReadResult.Failed(SetupImportFailureReason.Import, exception.Message);
        }
    }

    public async Task<SetupExportResult> ExportAsync(Setup setup, Bike bike, Guid? boardId, CancellationToken cancellationToken = default)
    {
        try
        {
            var file = await files.SaveSetupFileAsync(setup.Name);
            if (file is null) return new SetupExportResult.Canceled();
            await background.RunAsync(async () =>
            {
                var json = SetupFileFormat.Serialize(setup, bike, boardId);
                await using var stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream, Encoding.UTF8);
                await writer.WriteAsync(json.AsMemory(), cancellationToken);
            }, cancellationToken);
            return new SetupExportResult.Exported();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.Error(exception, "Setup export failed for {SetupId}", setup.Id);
            return new SetupExportResult.Failed(SetupExportFailureReason.Export, exception.Message);
        }
    }
}
