using System;
using System.Collections.Generic;
using System.IO;
using Sufni.Telemetry;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
namespace Sufni.App.Sessions.Processing.Services;

internal interface ISessionTelemetryProcessor
{
    TelemetryData ReadProcessedTelemetryData(byte[] processedData);

    SessionSummaryMetrics ComputeSummaryMetrics(double? durationSeconds, IReadOnlyList<TrackPoint>? points);
}

internal sealed class SessionTelemetryProcessor : ISessionTelemetryProcessor
{
    public TelemetryData ReadProcessedTelemetryData(byte[] processedData)
    {
        try
        {
            return TelemetryData.FromBinary(processedData)
                   ?? throw new InvalidDataException("Processed session data did not contain telemetry.");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException("Processed session data is not valid telemetry.", exception);
        }
    }

    public SessionSummaryMetrics ComputeSummaryMetrics(
        double? durationSeconds,
        IReadOnlyList<TrackPoint>? points) =>
        SessionSummaryMetricsCalculator.Calculate(durationSeconds, points);
}
