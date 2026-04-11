using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Sufni.App.BikeEditing;
using Sufni.App.Models;
using Sufni.Kinematics;

namespace Sufni.App.Services;

public sealed class BikeEditorService(IFilesService filesService, IBackgroundTaskRunner backgroundTaskRunner) : IBikeEditorService
{
    public async Task<BikeEditorAnalysisResult> AnalyzeLinkageAsync(
        Linkage? linkage,
        CancellationToken cancellationToken = default)
    {
        if (linkage is null)
        {
            return new BikeEditorAnalysisResult.Unavailable();
        }

        try
        {
            return await backgroundTaskRunner.RunAsync(() =>
            {
                try
                {
                    var solver = new KinematicSolver(linkage);
                    var solution = solver.SolveSuspensionMotion();
                    var characteristics = new BikeCharacteristics(solution);
                    var mapping = new JointNameMapping();
                    var rearAxlePathData = solution.TryGetValue(mapping.RearWheel, out var path)
                        ? path
                        : new CoordinateList([], []);
                    return (BikeEditorAnalysisResult)new BikeEditorAnalysisResult.Computed(
                        new BikeAnalysisPresentationData(
                            characteristics.LeverageRatioData,
                            rearAxlePathData));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    return new BikeEditorAnalysisResult.Unavailable();
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return new BikeEditorAnalysisResult.Failed(e.Message);
        }
    }

    public async Task<BikeImageLoadResult> LoadImageAsync(CancellationToken cancellationToken = default)
    {
        var file = await filesService.OpenBikeImageFileAsync();
        if (file is null)
        {
            return new BikeImageLoadResult.Canceled();
        }

        try
        {
            var bitmap = await backgroundTaskRunner.RunAsync(async () =>
            {
                await using var stream = await file.OpenReadAsync();
                return new Bitmap(stream);
            }, cancellationToken);

            return new BikeImageLoadResult.Loaded(bitmap);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return new BikeImageLoadResult.Failed(e.Message);
        }
    }

    public async Task<BikeFileImportResult> ImportBikeAsync(CancellationToken cancellationToken = default)
    {
        var file = await filesService.OpenBikeFileAsync();
        if (file is null)
        {
            return new BikeFileImportResult.Canceled();
        }

        try
        {
            return await backgroundTaskRunner.RunAsync<BikeFileImportResult>(async () =>
            {
                try
                {
                    await using var stream = await file.OpenReadAsync();
                    using var reader = new StreamReader(stream);
                    var json = await reader.ReadToEndAsync(cancellationToken);
                    var bike = Bike.FromJson(json);
                    return bike is null
                        ? new BikeFileImportResult.InvalidFile("JSON file was not a valid bike file.")
                        : (BikeFileImportResult)new BikeFileImportResult.Imported(bike);
                }
                catch (JsonException)
                {
                    return new BikeFileImportResult.InvalidFile("JSON file was not a valid bike file.");
                }
                catch (NotSupportedException)
                {
                    return new BikeFileImportResult.InvalidFile("JSON file was not a valid bike file.");
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return new BikeFileImportResult.Failed(e.Message);
        }
    }

    public async Task<BikeExportResult> ExportBikeAsync(Bike bike, CancellationToken cancellationToken = default)
    {
        var file = await filesService.SaveBikeFileAsync();
        if (file is null)
        {
            return new BikeExportResult.Canceled();
        }

        try
        {
            return await backgroundTaskRunner.RunAsync(async () =>
            {
                var bikeJson = bike.ToJson();
                await using var stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream, Encoding.UTF8);
                await writer.WriteAsync(bikeJson.AsMemory(), cancellationToken);
                return (BikeExportResult)new BikeExportResult.Exported();
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return new BikeExportResult.Failed(e.Message);
        }
    }
}