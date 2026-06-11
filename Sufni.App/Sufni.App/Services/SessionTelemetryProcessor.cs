using System;
using System.Collections.Generic;
using System.IO;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Services;

internal interface ISessionTelemetryProcessor
{
    double? ReadProcessedDurationSeconds(byte[]? processedData);

    TelemetryData ReadProcessedTelemetryData(byte[] processedData);

    SessionSummaryMetrics ComputeSummaryMetrics(double? durationSeconds, IReadOnlyList<TrackPoint>? points);

    List<TrackPoint>? GenerateSessionTrackFromFullTrack(
        Track fullTrack,
        long? timestamp,
        double? durationSeconds);
}

internal sealed class SessionTelemetryProcessor : ISessionTelemetryProcessor
{
    public double? ReadProcessedDurationSeconds(byte[]? processedData)
    {
        if (processedData is null)
        {
            return null;
        }

        try
        {
            return TelemetryData.FromBinary(processedData).Metadata?.Duration;
        }
        catch
        {
            return null;
        }
    }

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

    public List<TrackPoint>? GenerateSessionTrackFromFullTrack(
        Track fullTrack,
        long? timestamp,
        double? durationSeconds)
    {
        if (!timestamp.HasValue ||
            durationSeconds is not { } duration ||
            !double.IsFinite(duration) ||
            duration <= 0 ||
            !fullTrack.HasPoints)
        {
            return null;
        }

        var start = timestamp.Value;
        var end = start + (int)Math.Ceiling(duration);
        if (fullTrack.StartTime > start || fullTrack.EndTime < end)
        {
            return null;
        }

        var points = fullTrack.GenerateSessionTrack(start, end);
        return points.Count == 0 ? null : points;
    }
}
