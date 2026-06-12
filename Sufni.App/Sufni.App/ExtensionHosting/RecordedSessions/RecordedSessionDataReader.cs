using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHosting.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.ExtensionHosting.RecordedSessions;

internal sealed class RecordedSessionDataReader(
    ISessionRepository sessionRepository,
    ISessionTelemetryProcessor sessionTelemetryProcessor) : IRecordedSessionDataReader
{
    public async Task<IReadOnlyList<RecordedSessionCatalogItem>> GetSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sessions = await sessionRepository.GetSessionsAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return sessions
            .Select(session => new RecordedSessionCatalogItem(
                session.Id,
                session.Name,
                session.Timestamp,
                session.DurationSeconds))
            .ToArray();
    }

    public async Task<RecordedSessionCatalogItem?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = await sessionRepository.GetSessionAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        return session is null
            ? null
            : new RecordedSessionCatalogItem(
                session.Id,
                session.Name,
                session.Timestamp,
                session.DurationSeconds);
    }

    public async Task<TelemetryData?> GetProcessedTelemetryAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var raw = await sessionRepository.GetSessionRawPsstAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        return raw is null ? null : sessionTelemetryProcessor.ReadProcessedTelemetryData(raw);
    }

    public async Task<IReadOnlyList<TrackPoint>?> GetTrackAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var track = await sessionRepository.GetSessionTrackAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        return track;
    }
}
