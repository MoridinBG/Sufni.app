using System.Threading;
using ScottPlot;
using Sufni.App.Models;
using Sufni.App.Plots;
using Sufni.App.SessionDetails;
using Sufni.Telemetry;

namespace Sufni.App.Services;

public sealed class SessionPresentationService : ISessionPresentationService
{
    private const double HighSpeedThreshold = 200.0;

    public SessionDamperPercentages CalculateDamperPercentages(
        TelemetryData telemetryData,
        TelemetryTimeRange? range = null)
    {
        double? frontHsc = null;
        double? rearHsc = null;
        double? frontLsc = null;
        double? rearLsc = null;
        double? frontLsr = null;
        double? rearLsr = null;
        double? frontHsr = null;
        double? rearHsr = null;

        if (telemetryData.HasStrokeData(SuspensionType.Front, range))
        {
            var frontBands = telemetryData.CalculateVelocityBands(SuspensionType.Front, HighSpeedThreshold, range);
            frontHsc = frontBands.HighSpeedCompression;
            frontLsc = frontBands.LowSpeedCompression;
            frontLsr = frontBands.LowSpeedRebound;
            frontHsr = frontBands.HighSpeedRebound;
        }

        if (telemetryData.HasStrokeData(SuspensionType.Rear, range))
        {
            var rearBands = telemetryData.CalculateVelocityBands(SuspensionType.Rear, HighSpeedThreshold, range);
            rearHsc = rearBands.HighSpeedCompression;
            rearLsc = rearBands.LowSpeedCompression;
            rearLsr = rearBands.LowSpeedRebound;
            rearHsr = rearBands.HighSpeedRebound;
        }

        return new SessionDamperPercentages(
            frontHsc,
            rearHsc,
            frontLsc,
            rearLsc,
            frontLsr,
            rearLsr,
            frontHsr,
            rearHsr);
    }

    public SessionCachePresentationData BuildCachePresentation(
        TelemetryData telemetryData,
        SessionPresentationDimensions dimensions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var damperPercentages = CalculateDamperPercentages(telemetryData);

        string? frontTravelHistogram = null;
        string? rearTravelHistogram = null;
        string? frontVelocityHistogram = null;
        string? rearVelocityHistogram = null;
        string? compressionBalance = null;
        string? reboundBalance = null;

        if (telemetryData.HasStrokeData(SuspensionType.Front))
        {
            frontTravelHistogram = RenderTravelHistogram(telemetryData, SuspensionType.Front, dimensions);
            cancellationToken.ThrowIfCancellationRequested();

            frontVelocityHistogram = RenderVelocityHistogram(telemetryData, SuspensionType.Front, dimensions);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (telemetryData.HasStrokeData(SuspensionType.Rear))
        {
            rearTravelHistogram = RenderTravelHistogram(telemetryData, SuspensionType.Rear, dimensions);
            cancellationToken.ThrowIfCancellationRequested();

            rearVelocityHistogram = RenderVelocityHistogram(telemetryData, SuspensionType.Rear, dimensions);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var compressionBalanceAvailable = telemetryData.HasBalanceData(BalanceType.Compression);
        var reboundBalanceAvailable = telemetryData.HasBalanceData(BalanceType.Rebound);
        if (compressionBalanceAvailable)
        {
            compressionBalance = RenderBalance(telemetryData, BalanceType.Compression, dimensions);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (reboundBalanceAvailable)
        {
            reboundBalance = RenderBalance(telemetryData, BalanceType.Rebound, dimensions);
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
        SessionPresentationDimensions dimensions)
    {
        var plot = new VelocityHistogramPlot(new Plot(), type)
        {
            AverageMode = VelocityAverageMode.SampleAveraged,
        };
        plot.LoadTelemetryData(telemetryData);
        return plot.GetSvgXml(dimensions.VelocityHistogramWidth, dimensions.VelocityHistogramHeight);
    }

    private static string RenderBalance(
        TelemetryData telemetryData,
        BalanceType type,
        SessionPresentationDimensions dimensions)
    {
        var plot = new BalancePlot(new Plot(), type)
        {
            DisplacementMode = BalanceDisplacementMode.Zenith,
        };
        plot.LoadTelemetryData(telemetryData);
        return plot.GetSvgXml(dimensions.TravelHistogramWidth, dimensions.TravelHistogramHeight);
    }
}