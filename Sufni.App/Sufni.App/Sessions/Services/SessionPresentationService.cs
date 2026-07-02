using System.Threading;
using ScottPlot;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Plots;
namespace Sufni.App.Sessions.Services;

public sealed class SessionPresentationService : ISessionPresentationService
{
    public SessionDampingPercentages CalculateDampingPercentages(
        TelemetryData telemetryData,
        TelemetryTimeRange? range = null,
        VelocityAverageMode velocityAverageMode = VelocityAverageMode.SampleAveraged,
        DampingSpeedCutoffs? dampingSpeedCutoffs = null)
    {
        return RecordedSessionAnalysisComputer.CalculateDampingPercentages(
            telemetryData,
            range,
            velocityAverageMode,
            dampingSpeedCutoffs);
    }

    public SessionCachePresentationData BuildCachePresentation(
        TelemetryData telemetryData,
        SessionPresentationDimensions dimensions,
        CancellationToken cancellationToken = default,
        DampingSpeedCutoffs? dampingSpeedCutoffs = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cutoffs = dampingSpeedCutoffs ?? DampingSpeedCutoffs.Default;
        var dampingPercentages = CalculateDampingPercentages(telemetryData, dampingSpeedCutoffs: cutoffs);

        string? frontTravelDistribution = null;
        string? rearTravelDistribution = null;
        string? frontVelocityDistribution = null;
        string? rearVelocityDistribution = null;
        string? compressionBalance = null;
        string? reboundBalance = null;

        var frontTravel = CalculateTravelDistribution(telemetryData, SuspensionType.Front, cutoffs);
        var frontVelocity = CalculateVelocityDistribution(telemetryData, SuspensionType.Front, cutoffs);
        if (frontTravel.HasStrokeData)
        {
            frontTravelDistribution = RenderTravelDistribution(frontTravel, SuspensionType.Front, dimensions);
            cancellationToken.ThrowIfCancellationRequested();

            frontVelocityDistribution = RenderVelocityDistribution(frontVelocity, SuspensionType.Front, dimensions, cutoffs);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var rearTravel = CalculateTravelDistribution(telemetryData, SuspensionType.Rear, cutoffs);
        var rearVelocity = CalculateVelocityDistribution(telemetryData, SuspensionType.Rear, cutoffs);
        if (rearTravel.HasStrokeData)
        {
            rearTravelDistribution = RenderTravelDistribution(rearTravel, SuspensionType.Rear, dimensions);
            cancellationToken.ThrowIfCancellationRequested();

            rearVelocityDistribution = RenderVelocityDistribution(rearVelocity, SuspensionType.Rear, dimensions, cutoffs);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var canRenderBalance = frontTravel.HasStrokeData && rearTravel.HasStrokeData;
        var compressionBalanceResult = canRenderBalance
            ? CalculateBalance(telemetryData, BalanceType.Compression, cutoffs)
            : null;
        var reboundBalanceResult = canRenderBalance
            ? CalculateBalance(telemetryData, BalanceType.Rebound, cutoffs)
            : null;
        var compressionBalanceAvailable = compressionBalanceResult is not null &&
            HasRenderableBalanceData(compressionBalanceResult.Balance);
        var reboundBalanceAvailable = reboundBalanceResult is not null &&
            HasRenderableBalanceData(reboundBalanceResult.Balance);
        if (compressionBalanceAvailable)
        {
            compressionBalance = RenderBalance(compressionBalanceResult!, BalanceType.Compression, dimensions, cutoffs);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (reboundBalanceAvailable)
        {
            reboundBalance = RenderBalance(reboundBalanceResult!, BalanceType.Rebound, dimensions, cutoffs);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var balanceAvailable = compressionBalanceAvailable || reboundBalanceAvailable;

        return new SessionCachePresentationData(
            frontTravelDistribution,
            rearTravelDistribution,
            frontVelocityDistribution,
            rearVelocityDistribution,
            compressionBalance,
            reboundBalance,
            dampingPercentages,
            cutoffs,
            balanceAvailable);
    }

    private static string RenderTravelDistribution(
        TravelDistributionAnalysisResult data,
        SuspensionType type,
        SessionPresentationDimensions dimensions)
    {
        var plot = new TravelDistributionPlot(new Plot(), type)
        {
            HistogramMode = TravelDistributionMode.ActiveSuspension,
        };
        plot.LoadAnalysisData(data);
        return plot.GetSvgXml(dimensions.TravelDistributionWidth, dimensions.TravelDistributionHeight);
    }

    private static string RenderVelocityDistribution(
        VelocityDistributionAnalysisResult data,
        SuspensionType type,
        SessionPresentationDimensions dimensions,
        DampingSpeedCutoffs dampingSpeedCutoffs)
    {
        var plot = new VelocityDistributionPlot(new Plot(), type)
        {
            AverageMode = VelocityAverageMode.SampleAveraged,
            DampingSpeedCutoffs = dampingSpeedCutoffs,
        };
        plot.LoadAnalysisData(data);
        return plot.GetSvgXml(dimensions.VelocityDistributionWidth, dimensions.VelocityDistributionHeight);
    }

    private static string RenderBalance(
        BalanceAnalysisResult data,
        BalanceType type,
        SessionPresentationDimensions dimensions,
        DampingSpeedCutoffs dampingSpeedCutoffs)
    {
        var plot = new BalancePlot(new Plot(), type)
        {
            DisplacementMode = BalanceDisplacementMode.Zenith,
            DampingSpeedCutoffs = dampingSpeedCutoffs,
        };
        plot.LoadAnalysisData(data);
        return plot.GetSvgXml(dimensions.TravelDistributionWidth, dimensions.TravelDistributionHeight);
    }

    private static TravelDistributionAnalysisResult CalculateTravelDistribution(
        TelemetryData telemetryData,
        SuspensionType type,
        DampingSpeedCutoffs cutoffs) =>
        (TravelDistributionAnalysisResult)RecordedSessionAnalysisComputer.CalculateTravelDistribution(
            CreateCachePresentationKey(RecordedSessionAnalysisFamily.TravelDistribution, cutoffs, type),
            telemetryData);

    private static VelocityDistributionAnalysisResult CalculateVelocityDistribution(
        TelemetryData telemetryData,
        SuspensionType type,
        DampingSpeedCutoffs cutoffs) =>
        RecordedSessionAnalysisComputer.CalculateVelocityDistribution(
            CreateCachePresentationKey(RecordedSessionAnalysisFamily.VelocityDistribution, cutoffs, type),
            telemetryData);

    private static BalanceAnalysisResult CalculateBalance(
        TelemetryData telemetryData,
        BalanceType type,
        DampingSpeedCutoffs cutoffs) =>
        RecordedSessionAnalysisComputer.CalculateBalance(
            CreateCachePresentationKey(
                RecordedSessionAnalysisFamily.Balance,
                cutoffs,
                suspensionType: null,
                type),
            telemetryData);

    private static RecordedSessionAnalysisKey CreateCachePresentationKey(
        RecordedSessionAnalysisFamily family,
        DampingSpeedCutoffs cutoffs,
        SuspensionType? suspensionType,
        BalanceType? balanceType = null)
    {
        var inputs = new RecordedSessionAnalysisInputs(
            TelemetryGeneration: 0,
            AnalysisRange: null,
            TravelDistributionMode: TravelDistributionMode.ActiveSuspension,
            VelocityAverageMode: VelocityAverageMode.SampleAveraged,
            BalanceDisplacementMode: BalanceDisplacementMode.Zenith,
            BalanceSpeedMode: BalanceSpeedMode.Both,
            DampingSpeedCutoffs: cutoffs,
            DampingPercentages: SessionDampingPercentages.Empty,
            SessionInsightsTargetProfile: SessionInsightsTargetProfile.Trail);
        return inputs.CreateKey(family, suspensionType, balanceType);
    }

    private static bool HasRenderableBalanceData(BalanceData balance) =>
        balance.FrontTravel.Count >= 2 && balance.RearTravel.Count >= 2;
}
