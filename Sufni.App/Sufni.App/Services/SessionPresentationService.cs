using System.Threading;
using ScottPlot;
using Sufni.App.Models;
using Sufni.App.Plots;
using Sufni.App.SessionDetails;
using Sufni.Telemetry;

namespace Sufni.App.Services;

public sealed class SessionPresentationService : ISessionPresentationService
{
    public SessionDamperPercentages CalculateDamperPercentages(
        TelemetryData telemetryData,
        TelemetryTimeRange? range = null,
        VelocityAverageMode velocityAverageMode = VelocityAverageMode.SampleAveraged,
        DampingSpeedCutoffs? dampingSpeedCutoffs = null)
    {
        var cutoffs = dampingSpeedCutoffs ?? DampingSpeedCutoffs.Default;
        return SessionDamperPercentages.FromSides(
            CalculateDamperSidePercentages(telemetryData, SuspensionType.Front, range, velocityAverageMode, cutoffs.Front),
            CalculateDamperSidePercentages(telemetryData, SuspensionType.Rear, range, velocityAverageMode, cutoffs.Rear));
    }

    private static SessionDamperSidePercentages CalculateDamperSidePercentages(
        TelemetryData telemetryData,
        SuspensionType suspensionType,
        TelemetryTimeRange? range,
        VelocityAverageMode velocityAverageMode,
        DampingSpeedCutoffSide cutoffs)
    {
        if (!TelemetryStatistics.HasStrokeData(telemetryData, suspensionType, range))
        {
            return SessionDamperSidePercentages.Empty;
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
        return new SessionDamperSidePercentages(
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
        var damperPercentages = CalculateDamperPercentages(telemetryData, dampingSpeedCutoffs: cutoffs);

        string? frontTravelHistogram = null;
        string? rearTravelHistogram = null;
        string? frontVelocityHistogram = null;
        string? rearVelocityHistogram = null;
        string? compressionBalance = null;
        string? reboundBalance = null;

        if (TelemetryStatistics.HasStrokeData(telemetryData, SuspensionType.Front))
        {
            frontTravelHistogram = RenderTravelHistogram(telemetryData, SuspensionType.Front, dimensions);
            cancellationToken.ThrowIfCancellationRequested();

            frontVelocityHistogram = RenderVelocityHistogram(telemetryData, SuspensionType.Front, dimensions, cutoffs);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (TelemetryStatistics.HasStrokeData(telemetryData, SuspensionType.Rear))
        {
            rearTravelHistogram = RenderTravelHistogram(telemetryData, SuspensionType.Rear, dimensions);
            cancellationToken.ThrowIfCancellationRequested();

            rearVelocityHistogram = RenderVelocityHistogram(telemetryData, SuspensionType.Rear, dimensions, cutoffs);
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
            frontTravelHistogram,
            rearTravelHistogram,
            frontVelocityHistogram,
            rearVelocityHistogram,
            compressionBalance,
            reboundBalance,
            damperPercentages,
            cutoffs,
            balanceAvailable);
    }

    private static string RenderTravelHistogram(
        TelemetryData telemetryData,
        SuspensionType type,
        SessionPresentationDimensions dimensions)
    {
        var plot = new TravelHistogramPlot(new Plot(), type)
        {
            HistogramMode = TravelHistogramMode.ActiveSuspension,
        };
        plot.LoadTelemetryData(telemetryData);
        return plot.GetSvgXml(dimensions.TravelHistogramWidth, dimensions.TravelHistogramHeight);
    }

    private static string RenderVelocityHistogram(
        TelemetryData telemetryData,
        SuspensionType type,
        SessionPresentationDimensions dimensions,
        DampingSpeedCutoffs dampingSpeedCutoffs)
    {
        var plot = new VelocityHistogramPlot(new Plot(), type)
        {
            AverageMode = VelocityAverageMode.SampleAveraged,
            DampingSpeedCutoffs = dampingSpeedCutoffs,
        };
        plot.LoadTelemetryData(telemetryData);
        return plot.GetSvgXml(dimensions.VelocityHistogramWidth, dimensions.VelocityHistogramHeight);
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
        return plot.GetSvgXml(dimensions.TravelHistogramWidth, dimensions.TravelHistogramHeight);
    }
}
