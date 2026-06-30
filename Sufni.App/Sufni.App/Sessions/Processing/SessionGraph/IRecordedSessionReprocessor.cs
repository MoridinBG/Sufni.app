using System.Threading;
using System.Threading.Tasks;
using Sufni.Telemetry;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
namespace Sufni.App.Sessions.Processing.SessionGraph;

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
public sealed record RecordedSessionReprocessResult(
    TelemetryData TelemetryData,
    Track? GeneratedFullTrack,
    ProcessingFingerprint Fingerprint);
