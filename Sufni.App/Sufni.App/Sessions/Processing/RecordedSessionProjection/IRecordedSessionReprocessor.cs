using System.Threading;
using System.Threading.Tasks;
using Sufni.Telemetry;
using Sufni.App.Shared.Models;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

// Rebuilds derived telemetry from a durable raw source. The domain snapshot
// supplies the setup/bike context, while processing options choose the pipeline.
public interface IRecordedSessionReprocessor
{
    Task<RecordedSessionReprocessResult> ReprocessAsync(
        RecordedSessionDomainSnapshot domain,
        RecordedSessionSource source,
        CancellationToken cancellationToken = default);

    Task<RecordedSessionReprocessResult> ReprocessAsync(
        RecordedSessionDomainSnapshot domain,
        RecordedSessionSource source,
        TelemetryProcessingOptions processingOptions,
        CancellationToken cancellationToken = default);
}

// Result of a reprocess pass: processed telemetry, an optional regenerated
// track, and the fingerprint for the exact inputs that produced the cache.
public sealed record ProcessedTelemetryPayload(
    TelemetryData TelemetryData,
    byte[] Data,
    string? FingerprintJson);

public abstract record RecordedSessionReprocessResult
{
    private RecordedSessionReprocessResult() { }
    public sealed record Processed(
        ProcessedTelemetryPayload ProcessedTelemetry,
        Track? GeneratedFullTrack,
        ProcessingFingerprint Fingerprint) : RecordedSessionReprocessResult;
    public sealed record Failed(SessionProcessingFailureReason Reason, string? Details = null) : RecordedSessionReprocessResult;
}
