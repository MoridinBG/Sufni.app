using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.SessionGraph;

/// <summary>
/// Defines reprocessing of a recorded raw source into derived telemetry data.
/// A successful result contains the rebuilt processed telemetry, any generated
/// full track, and the fingerprint for the inputs that produced them.
/// </summary>
public interface IRecordedSessionReprocessor
{
    Task<RecordedSessionReprocessResult> ReprocessAsync(
        RecordedSessionDomainSnapshot domain,
        RecordedSessionSource source,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of rebuilding derived data from a recorded raw source.
/// The value contains the processed telemetry cache payload, optional generated
/// track, and fingerprint that describes the rebuilt cache.
/// </summary>
public sealed record RecordedSessionReprocessResult(
    TelemetryData TelemetryData,
    Track? GeneratedFullTrack,
    ProcessingFingerprint Fingerprint);
