using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.SessionGraph;

public interface IRecordedSessionReprocessor
{
    Task<RecordedSessionReprocessResult> ReprocessAsync(
        RecordedSessionDomainSnapshot domain,
        RecordedSessionSource source,
        CancellationToken cancellationToken = default);
}

public sealed record RecordedSessionReprocessResult(
    TelemetryData TelemetryData,
    Track? GeneratedFullTrack,
    ProcessingFingerprint Fingerprint);
