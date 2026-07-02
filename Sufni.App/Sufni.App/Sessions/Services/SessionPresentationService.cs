using System.Threading;
using ScottPlot;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

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
        var cutoffs = dampingSpeedCutoffs ?? DampingSpeedCutoffs.Default;
        return SessionDampingPercentages.FromSides(
            CalculateDamperSidePercentages(telemetryData, SuspensionType.Front, range, velocityAverageMode, cutoffs.Front),
            CalculateDamperSidePercentages(telemetryData, SuspensionType.Rear, range, velocityAverageMode, cutoffs.Rear));
    }

    private static SessionDampingSidePercentages CalculateDamperSidePercentages(
        TelemetryData telemetryData,
        SuspensionType suspensionType,
        TelemetryTimeRange? range,
        VelocityAverageMode velocityAverageMode,
        DampingSpeedCutoffSide cutoffs)
    {
        if (!TelemetryStatistics.HasStrokeData(telemetryData, suspensionType, range))
        {
            return SessionDampingSidePercentages.Empty;
        }

        var options = new VelocityStatisticsOptions(
            range,
            velocityAverageMode,
            cutoffs.CompressionMmPerSecond,
            cutoffs.ReboundMmPerSecond);
        var bands = TelemetryStatistics.CalculateVelocityBands(
            telemetryData,
            suspensionType,
            options);
        return new SessionDampingSidePercentages(
            bands.HighSpeedCompression,
            bands.LowSpeedCompression,
            bands.LowSpeedRebound,
            bands.HighSpeedRebound);
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

        if (TelemetryStatistics.HasStrokeData(telemetryData, SuspensionType.Front))
        {
            frontTravelDistribution = RenderTravelDistribution(telemetryData, SuspensionType.Front, dimensions);
            cancellationToken.ThrowIfCancellationRequested();

            frontVelocityDistribution = RenderVelocityDistribution(telemetryData, SuspensionType.Front, dimensions, cutoffs);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (TelemetryStatistics.HasStrokeData(telemetryData, SuspensionType.Rear))
        {
            rearTravelDistribution = RenderTravelDistribution(telemetryData, SuspensionType.Rear, dimensions);
            cancellationToken.ThrowIfCancellationRequested();

            rearVelocityDistribution = RenderVelocityDistribution(telemetryData, SuspensionType.Rear, dimensions, cutoffs);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var compressionBalanceAvailable = TelemetryStatistics.HasBalanceData(telemetryData, BalanceType.Compression);
        var reboundBalanceAvailable = TelemetryStatistics.HasBalanceData(telemetryData, BalanceType.Rebound);
        if (compressionBalanceAvailable)
        {
            compressionBalance = RenderBalance(telemetryData, BalanceType.Compression, dimensions, cutoffs);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (reboundBalanceAvailable)
        {
            reboundBalance = RenderBalance(telemetryData, BalanceType.Rebound, dimensions, cutoffs);
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
        TelemetryData telemetryData,
        SuspensionType type,
        SessionPresentationDimensions dimensions)
    {
        var plot = new TravelDistributionPlot(new Plot(), type)
        {
            HistogramMode = TravelDistributionMode.ActiveSuspension,
        };
        plot.LoadTelemetryData(telemetryData);
        return plot.GetSvgXml(dimensions.TravelDistributionWidth, dimensions.TravelDistributionHeight);
    }

    private static string RenderVelocityDistribution(
        TelemetryData telemetryData,
        SuspensionType type,
        SessionPresentationDimensions dimensions,
        DampingSpeedCutoffs dampingSpeedCutoffs)
    {
        var plot = new VelocityDistributionPlot(new Plot(), type)
        {
            AverageMode = VelocityAverageMode.SampleAveraged,
            DampingSpeedCutoffs = dampingSpeedCutoffs,
        };
        plot.LoadTelemetryData(telemetryData);
        return plot.GetSvgXml(dimensions.VelocityDistributionWidth, dimensions.VelocityDistributionHeight);
    }

    private static string RenderBalance(
        TelemetryData telemetryData,
        BalanceType type,
        SessionPresentationDimensions dimensions,
        DampingSpeedCutoffs dampingSpeedCutoffs)
    {
        var plot = new BalancePlot(new Plot(), type)
        {
            DisplacementMode = BalanceDisplacementMode.Zenith,
            DampingSpeedCutoffs = dampingSpeedCutoffs,
        };
        plot.LoadTelemetryData(telemetryData);
        return plot.GetSvgXml(dimensions.TravelDistributionWidth, dimensions.TravelDistributionHeight);
    }
}
