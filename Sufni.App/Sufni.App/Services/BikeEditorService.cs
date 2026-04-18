using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Sufni.App.BikeEditing;
using Sufni.App.Models;
using Sufni.Kinematics;
using Serilog;

namespace Sufni.App.Services;

public sealed class BikeEditorService(IFilesService filesService, IBackgroundTaskRunner backgroundTaskRunner) : IBikeEditorService
{
    private static readonly ILogger logger = Log.ForContext<BikeEditorService>();

    public async Task<BikeEditorAnalysisResult> LoadAnalysisAsync(
        RearSuspension? rearSuspension,
        CancellationToken cancellationToken = default)
    {
        if (rearSuspension is null)
        {
            logger.Verbose("Skipping bike rear suspension analysis because no rear suspension was supplied");
            return new BikeEditorAnalysisResult.Unavailable();
        }

        try
        {
            logger.Verbose("Starting bike rear suspension analysis for {RearSuspensionType}", rearSuspension.GetType().Name);

            return await backgroundTaskRunner.RunAsync(() =>
            {
                try
                {
                    return rearSuspension switch
                    {
                        LinkageRearSuspension linkageRearSuspension => AnalyzeLinkage(linkageRearSuspension.Linkage),
                        LeverageRatioRearSuspension leverageRatioRearSuspension => AnalyzeLeverageRatio(leverageRatioRearSuspension.LeverageRatio),
                        _ => throw new ArgumentOutOfRangeException(nameof(rearSuspension))
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.Warning(exception, "Bike rear suspension analysis returned unavailable");
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
            logger.Error(e, "Bike rear suspension analysis failed");
            return new BikeEditorAnalysisResult.Failed(e.Message);
        }
    }

    public async Task<LeverageRatioImportResult> ImportLeverageRatioAsync(CancellationToken cancellationToken = default)
    {
        logger.Verbose("Opening leverage ratio CSV picker");
        var file = await filesService.OpenLeverageRatioCsvFileAsync();
        if (file is null)
        {
            logger.Verbose("Leverage ratio CSV import canceled");
            return new LeverageRatioImportResult.Canceled();
        }

        try
        {
            return await backgroundTaskRunner.RunAsync<LeverageRatioImportResult>(async () =>
            {
                await using var stream = await file.OpenReadAsync();
                var parseResult = LeverageRatioCsvParser.Parse(stream);
                return parseResult switch
                {
                    LeverageRatioParseResult.Parsed parsed => new LeverageRatioImportResult.Imported(parsed.Value),
                    LeverageRatioParseResult.Invalid invalid => new LeverageRatioImportResult.Invalid(
                        invalid.Errors
                            .Select(error => error.LineNumber.HasValue
                                ? $"Line {error.LineNumber.Value}: {error.Message}"
                                : error.Message)
                            .ToArray()),
                    _ => new LeverageRatioImportResult.Failed("CSV file could not be parsed.")
                };
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Leverage ratio CSV import failed");
            return new LeverageRatioImportResult.Failed(e.Message);
        }
    }

    public async Task<BikeImageLoadResult> LoadImageAsync(CancellationToken cancellationToken = default)
    {
        logger.Verbose("Opening bike image picker");
        var file = await filesService.OpenBikeImageFileAsync();
        if (file is null)
        {
            logger.Verbose("Bike image load canceled");
            return new BikeImageLoadResult.Canceled();
        }

        try
        {
            var loadedImage = await backgroundTaskRunner.RunAsync(async () =>
            {
                await using var stream = await file.OpenReadAsync();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                var imageBytes = buffer.ToArray();
                var bitmap = BikeImageData.Decode(imageBytes) ?? throw new InvalidOperationException("Bike image could not be decoded.");
                return new BikeImageLoadResult.Loaded(imageBytes, bitmap);
            }, cancellationToken);

            logger.Verbose(
                "Bike image loaded with width {PixelWidth} and height {PixelHeight}",
                loadedImage.Bitmap.PixelSize.Width,
                loadedImage.Bitmap.PixelSize.Height);

            return loadedImage;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Bike image load failed");
            return new BikeImageLoadResult.Failed(e.Message);
        }
    }

    public async Task<BikeFileImportResult> ImportBikeAsync(CancellationToken cancellationToken = default)
    {
        logger.Verbose("Opening bike import file picker");
        var file = await filesService.OpenBikeFileAsync();
        if (file is null)
        {
            logger.Verbose("Bike import canceled");
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

                    if (bike is null)
                    {
                        logger.Warning("Bike import rejected because the selected JSON file was not a valid bike file");
                        return new BikeFileImportResult.InvalidFile("JSON file was not a valid bike file.");
                    }

                    logger.Verbose("Bike import parsed bike {BikeId}", bike.Id);
                    return new BikeFileImportResult.Imported(bike);
                }
                catch (JsonException exception)
                {
                    logger.Warning(exception, "Bike import rejected because the selected file was invalid JSON");
                    return new BikeFileImportResult.InvalidFile("JSON file was not a valid bike file.");
                }
                catch (NotSupportedException exception)
                {
                    logger.Warning(exception, "Bike import rejected because the selected file format was unsupported");
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
            logger.Error(e, "Bike import failed");
            return new BikeFileImportResult.Failed(e.Message);
        }
    }

    public async Task<BikeExportResult> ExportBikeAsync(Bike bike, CancellationToken cancellationToken = default)
    {
        logger.Verbose("Opening bike export file picker for {BikeId}", bike.Id);
        var file = await filesService.SaveBikeFileAsync();
        if (file is null)
        {
            logger.Verbose("Bike export canceled for {BikeId}", bike.Id);
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

                logger.Verbose(
                    "Bike export wrote {ByteCount} UTF-8 characters for {BikeId}",
                    bikeJson.Length,
                    bike.Id);

                return (BikeExportResult)new BikeExportResult.Exported();
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Bike export failed for {BikeId}", bike.Id);
            return new BikeExportResult.Failed(e.Message);
        }
    }

    private BikeEditorAnalysisResult AnalyzeLinkage(Linkage linkage)
    {
        var solver = new KinematicSolver(linkage);
        var solution = solver.SolveSuspensionMotion();
        var characteristics = new BikeCharacteristics(solution);
        var mapping = new JointNameMapping();
        var rearAxlePathData = solution.TryGetValue(mapping.RearWheel, out var path)
            ? path
            : new CoordinateList([], []);

        logger.Verbose(
            "Bike linkage analysis computed {LeveragePointCount} leverage points and {RearAxlePointCount} rear axle points",
            characteristics.LeverageRatioData.X.Count,
            rearAxlePathData.X.Count);

        return new BikeEditorAnalysisResult.Computed(
            new BikeAnalysisPresentationData(
                characteristics.LeverageRatioData,
                rearAxlePathData));
    }

    private BikeEditorAnalysisResult AnalyzeLeverageRatio(LeverageRatio leverageRatio)
    {
        var samples = leverageRatio.DeriveLeverageRatioSamples();
        var coordinateList = new CoordinateList(
            [.. samples.Select(sample => sample.WheelTravelMm)],
            [.. samples.Select(sample => sample.Ratio)]);

        logger.Verbose(
            "Bike leverage ratio analysis computed {LeveragePointCount} leverage points",
            coordinateList.X.Count);

        return new BikeEditorAnalysisResult.Computed(
            new BikeAnalysisPresentationData(coordinateList, null));
    }
}