using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.Queries;

public interface IRearSuspensionAnalysisQuery
{
    Task<BikeEditorAnalysisResult> LoadAnalysisAsync(RearSuspensionSpec rearSuspension,
        CancellationToken cancellationToken = default);
}

internal sealed class RearSuspensionAnalysisQuery(IBackgroundTaskRunner backgroundTaskRunner,
    IKinematicSolutionCache kinematicSolutionCache) : IRearSuspensionAnalysisQuery
{
    private static readonly ILogger logger = Log.ForContext<RearSuspensionAnalysisQuery>();

    public async Task<BikeEditorAnalysisResult> LoadAnalysisAsync(
        RearSuspensionSpec rearSuspension,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rearSuspension);

        if (rearSuspension is RearSuspensionSpec.Hardtail or RearSuspensionSpec.LinkageDraft or RearSuspensionSpec.LeverageRatioDraft)
        {
            logger.Verbose("Skipping bike rear suspension analysis for {RearSuspensionType}", rearSuspension.GetType().Name);
            return rearSuspension is RearSuspensionSpec.Hardtail
                ? new BikeEditorAnalysisResult.NotApplicable()
                : new BikeEditorAnalysisResult.Unsolvable();
        }

        try
        {
            logger.Verbose("Starting bike rear suspension analysis for {RearSuspensionType}", rearSuspension.GetType().Name);

            return await backgroundTaskRunner.RunAsync(() => rearSuspension switch
            {
                RearSuspensionSpec.Linkage linkage => AnalyzeLinkage(linkage.Spec),
                RearSuspensionSpec.LeverageRatio leverageRatio => AnalyzeLeverageRatio(leverageRatio.Spec),
                _ => throw new ArgumentOutOfRangeException(nameof(rearSuspension))
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (e is LinkageValidationException or LeverageRatioValidationException or KinematicCalculationException)
        {
            logger.Warning(e, "Bike rear suspension analysis is unsolvable");
            return new BikeEditorAnalysisResult.Unsolvable();
        }
        catch (Exception e)
        {
            logger.Error(e, "Bike rear suspension analysis failed");
            return new BikeEditorAnalysisResult.Failed(BikeAnalysisFailureReason.Analyze, e.Message);
        }
    }

    private BikeEditorAnalysisResult AnalyzeLinkage(LinkageSpec linkage)
    {
        var solution = kinematicSolutionCache.GetOrSolve(linkage);
        var characteristics = new BikeCharacteristics(solution);
        var rearAxlePathData = solution.TryGetPath(JointNames.RearWheel, out var path)
            ? new CoordinateList([.. path.X], [.. path.Y])
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

    private BikeEditorAnalysisResult AnalyzeLeverageRatio(LeverageRatioSpec leverageRatio)
    {
        var coordinateList = leverageRatio.DeriveLeverageRatioData();

        logger.Verbose(
            "Bike leverage ratio analysis computed {LeveragePointCount} leverage points",
            coordinateList.X.Count);

        return new BikeEditorAnalysisResult.Computed(
            new BikeAnalysisPresentationData(coordinateList, null));
    }
}
