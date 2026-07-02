using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.Telemetry;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
namespace Sufni.App.Tests.TestSupport.Doubles;

/// <summary>
/// Hand-written <see cref="ISessionTelemetryProcessor"/> stand-in for tests.
/// The interface is internal, so NSubstitute cannot proxy it; this stub lets
/// tests map raw psst bytes to specific <see cref="TelemetryData"/> instances
/// (preserving reference identity through the loader) while delegating
/// everything unmapped to the real processor.
/// </summary>
internal sealed class TestSessionTelemetryProcessor : ISessionTelemetryProcessor
{
    private readonly SessionTelemetryProcessor real = new();
    private readonly Dictionary<byte[], TelemetryData> mappedTelemetry = new(ReferenceEqualityComparer.Instance);

    public int ReadProcessedDurationSecondsCallCount { get; private set; }

    public int ReadProcessedTelemetryDataCallCount { get; private set; }

    public void Map(byte[] raw, TelemetryData telemetry) => mappedTelemetry[raw] = telemetry;

    public double? ReadProcessedDurationSeconds(byte[]? processedData)
    {
        ReadProcessedDurationSecondsCallCount++;
        return processedData is not null && mappedTelemetry.TryGetValue(processedData, out var telemetry)
            ? telemetry.Metadata?.Duration
            : real.ReadProcessedDurationSeconds(processedData);
    }

    public TelemetryData ReadProcessedTelemetryData(byte[] processedData)
    {
        ReadProcessedTelemetryDataCallCount++;
        return mappedTelemetry.TryGetValue(processedData, out var telemetry)
            ? telemetry
            : real.ReadProcessedTelemetryData(processedData);
    }

    public SessionSummaryMetrics ComputeSummaryMetrics(double? durationSeconds, IReadOnlyList<TrackPoint>? points) =>
        real.ComputeSummaryMetrics(durationSeconds, points);

    public List<TrackPoint>? GenerateSessionTrackFromFullTrack(
        Track fullTrack,
        long? timestamp,
        double? durationSeconds,
        double gpsOffsetSeconds = 0) =>
        real.GenerateSessionTrackFromFullTrack(fullTrack, timestamp, durationSeconds, gpsOffsetSeconds);
}
