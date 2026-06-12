using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Infrastructure;

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

    public void Map(byte[] raw, TelemetryData telemetry) => mappedTelemetry[raw] = telemetry;

    public double? ReadProcessedDurationSeconds(byte[]? processedData) =>
        processedData is not null && mappedTelemetry.TryGetValue(processedData, out var telemetry)
            ? telemetry.Metadata?.Duration
            : real.ReadProcessedDurationSeconds(processedData);

    public TelemetryData ReadProcessedTelemetryData(byte[] processedData) =>
        mappedTelemetry.TryGetValue(processedData, out var telemetry)
            ? telemetry
            : real.ReadProcessedTelemetryData(processedData);

    public SessionSummaryMetrics ComputeSummaryMetrics(double? durationSeconds, IReadOnlyList<TrackPoint>? points) =>
        real.ComputeSummaryMetrics(durationSeconds, points);

    public List<TrackPoint>? GenerateSessionTrackFromFullTrack(
        Track fullTrack,
        long? timestamp,
        double? durationSeconds) =>
        real.GenerateSessionTrackFromFullTrack(fullTrack, timestamp, durationSeconds);
}
