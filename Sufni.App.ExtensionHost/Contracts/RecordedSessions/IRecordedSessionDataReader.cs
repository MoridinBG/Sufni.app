using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.Telemetry;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

public interface IRecordedSessionDataReader
{
    Task<IReadOnlyList<RecordedSessionCatalogItem>> GetSessionsAsync(
        CancellationToken cancellationToken = default);

    Task<RecordedSessionCatalogItem?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<TelemetryData?> GetProcessedTelemetryAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackPoint>?> GetTrackAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<RecordedSessionContentSnapshotResult> GetExactSnapshotAsync(
        Guid sessionId,
        RecordedSessionContentToken expectedToken,
        RecordedSessionContentSelection selection,
        CancellationToken cancellationToken = default);
}
